// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Caster.Api.Tests.Support;

namespace Caster.Api.Tests.Infrastructure.Swashbuckle;

/// <summary>
/// The corrected routes are what the swagger document offers a client generator; the stacked routes they
/// replace stay as aliases, with operation ids of their own and marked deprecated.
/// </summary>
public class ObsoleteRouteSwaggerTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    [Theory]
    [InlineData("/api/projects/{id}/actions/export", "get", "ExportProject")]
    [InlineData("/api/projects/{id}/actions/import", "post", "ImportProject")]
    public async Task The_corrected_route_carries_the_operation_id_and_is_not_deprecated(
        string path, string method, string operationId)
    {
        using var document = await SwaggerDocument();

        var operation = Operation(document, path, method);

        Assert.Equal(operationId, operation.GetProperty("operationId").GetString());
        Assert.False(operation.TryGetProperty("deprecated", out var deprecated) && deprecated.GetBoolean());
    }

    [Theory]
    [InlineData("/api/projects/projects/{id}/actions/export", "get", "ExportProjectObsolete")]
    [InlineData("/api/projects/projects/{id}/actions/import", "post", "ImportProjectObsolete")]
    public async Task The_obsolete_alias_has_its_own_operation_id_and_is_deprecated(
        string path, string method, string operationId)
    {
        using var document = await SwaggerDocument();

        var operation = Operation(document, path, method);

        Assert.Equal(operationId, operation.GetProperty("operationId").GetString());
        Assert.True(operation.GetProperty("deprecated").GetBoolean());
    }

    private async Task<JsonDocument> SwaggerDocument()
    {
        var response = await Client().GetAsync("/swagger/v1/swagger.json", Ct);
        await AssertStatus(HttpStatusCode.OK, response);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
    }

    private static JsonElement Operation(JsonDocument document, string path, string method)
    {
        Assert.True(document.RootElement.GetProperty("paths").TryGetProperty(path, out var item), $"no path {path}");
        Assert.True(item.TryGetProperty(method, out var operation), $"no {method} on {path}");

        return operation;
    }
}
