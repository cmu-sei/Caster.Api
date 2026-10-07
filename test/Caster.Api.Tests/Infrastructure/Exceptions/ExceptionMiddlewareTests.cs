// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;

namespace Caster.Api.Tests.Infrastructure.Exceptions;

/// <summary>
/// The <c>application/problem+json</c> shapes clients parse: <c>ExceptionMiddleware</c> maps each
/// <c>IApiException</c> to its status with the message as the title, a validation failure to a 400 with its
/// errors, and anything else to a 500 whose detail is the message (the host runs as Production). The MVC
/// <c>[ApiController]</c> filter answers malformed bodies and route values before a handler runs.
/// </summary>
public class ExceptionMiddlewareTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    [Fact]
    public async Task A_forbidden_handler_is_a_403_titled_insufficient_permissions()
    {
        var project = TestData.Project();
        await Seed(project);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewHosts).SeedAsync();

        var problem = await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/projects/{project.Id}", Ct));

        Assert.Equal("Insufficient Permissions", problem.Title);
        Assert.Equal(403, problem.Status);
    }

    [Fact]
    public async Task A_missing_entity_is_a_404_titled_with_its_type()
    {
        var problem = await AssertProblem(HttpStatusCode.NotFound, await RootClient.GetAsync($"api/hosts/{Guid.NewGuid()}", Ct));

        Assert.Equal("Host not found", problem.Title);
    }

    [Fact]
    public async Task A_conflict_is_a_409_titled_with_its_message()
    {
        var problem = await AssertProblem(HttpStatusCode.Conflict, await RootClient.DeleteAsync($"api/system-roles/{TestData.Roles.Administrator}", Ct));

        Assert.Equal("Immutable Role cannot be deleted.", problem.Title);
    }

    [Fact]
    public async Task A_validation_failure_is_a_400_listing_the_failing_property()
    {
        var response = await RootClient.PostAsJsonAsync("api/files", new { name = "../escape.tf", directoryId = Guid.NewGuid() }, Ct);

        await AssertProblem(HttpStatusCode.BadRequest, response);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("Name", out _));
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("DirectoryId", out _));
    }

    [Fact]
    public async Task An_unhandled_exception_is_a_500_whose_detail_is_the_message_without_a_stack_trace()
    {
        // Same case as Get_with_a_malformed_id_answers_with_a_server_error.
        var problem = await AssertProblem(HttpStatusCode.InternalServerError, await RootClient.GetAsync("api/modules/not-a-guid", Ct));

        Assert.Equal("A server error occurred.", problem.Title);
        Assert.DoesNotContain(" at ", problem.Detail);
    }

    [Fact]
    public async Task A_malformed_json_body_is_a_400_before_any_handler_runs()
    {
        using var content = new StringContent("{ not json", Encoding.UTF8, "application/json");

        await AssertProblem(HttpStatusCode.BadRequest, await RootClient.PostAsync("api/projects", content, Ct));
    }

    [Fact]
    public async Task A_missing_body_is_a_400()
    {
        using var content = new StringContent(string.Empty, Encoding.UTF8, "application/json");

        await AssertProblem(HttpStatusCode.BadRequest, await RootClient.PostAsync("api/projects", content, Ct));
    }

    [Fact]
    public async Task A_route_id_that_is_not_a_guid_is_a_400()
    {
        await AssertProblem(HttpStatusCode.BadRequest, await RootClient.GetAsync("api/projects/not-a-guid", Ct));
    }

    [Fact]
    public async Task An_enum_value_outside_the_enum_by_name_is_a_400()
    {
        var response = await RootClient.PostAsJsonAsync("api/system-roles", new { name = "bad-permission", permissions = new[] { "NotAPermission" } }, Ct);

        await AssertProblem(HttpStatusCode.BadRequest, response);
    }

    /// <summary>A request with no identity is answered with a 401; the anonymous client is the case under test.</summary>
    [Fact]
    public async Task A_request_without_an_identity_to_a_write_is_unauthorized()
    {
        await AssertStatus(HttpStatusCode.Unauthorized, await Client().PostAsJsonAsync("api/projects", new { name = "anonymous" }, Ct));
    }
}
