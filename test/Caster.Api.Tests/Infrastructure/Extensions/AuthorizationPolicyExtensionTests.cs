// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;

namespace Caster.Api.Tests.Infrastructure.Extensions;

/// <summary>
/// The default policy <c>AuthorizationPolicyExtensions.AddAuthorizationPolicy</c> builds: an authenticated
/// user whose token carries every scope in <c>Authorization:AuthorizationScope</c> ("caster" as shipped).
/// Every controller takes it through its <c>[Authorize]</c>, and the Prometheus endpoint through
/// <c>MapPrometheusScrapingEndpoint().RequireAuthorization()</c> in <c>Startup.Configure</c>; the hub's
/// connection is in <c>ProjectHubConnectionTests</c>.
/// </summary>
public class AuthorizationPolicyExtensionTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    /// <summary>A token scoped for another API only, the scope the default policy does not accept.</summary>
    private const string AnotherApisScope = "player";

    /// <summary>Where <c>MapPrometheusScrapingEndpoint()</c> serves the metrics by default.</summary>
    private const string MetricsPath = "/metrics";

    /// <summary>
    /// The default policy refuses a token without the caster scope before any handler runs, so the 403 has
    /// no body (a handler's refusal is a problem document).
    /// </summary>
    [Fact]
    public async Task Get_is_forbidden_for_a_member_whose_token_lacks_the_caster_scope_but_allowed_with_it()
    {
        var project = TestData.Project();
        await Seed(project);
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/projects/{project.Id}");
        request.Headers.Add(TestAuthHandler.ScopeHeader, AnotherApisScope);

        var refused = await Client(actor).SendAsync(request, Ct);

        await AssertStatus(HttpStatusCode.Forbidden, refused);
        Assert.Empty(await refused.Content.ReadAsStringAsync(Ct));
        await AssertStatus(HttpStatusCode.OK, await Client(actor).GetAsync($"api/projects/{project.Id}", Ct));
    }

    /// <summary>The metrics need an authenticated caller with the caster scope and no permission.</summary>
    [Fact]
    public async Task Metrics_are_served_to_an_actor()
    {
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).GetAsync(MetricsPath, Ct));
    }

    [Fact]
    public async Task Metrics_without_an_identity_are_unauthorized()
    {
        await AssertStatus(HttpStatusCode.Unauthorized, await Client().GetAsync(MetricsPath, Ct));
    }

    /// <summary>
    /// The default policy refuses an actor whose token lacks the caster scope;
    /// <see cref="Metrics_are_served_to_an_actor"/> is its control.
    /// </summary>
    [Fact]
    public async Task Metrics_are_forbidden_for_an_actor_whose_token_lacks_the_caster_scope()
    {
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, MetricsPath);
        request.Headers.Add(TestAuthHandler.ScopeHeader, AnotherApisScope);

        await AssertStatus(HttpStatusCode.Forbidden, await Client(actor).SendAsync(request, Ct));
    }
}
