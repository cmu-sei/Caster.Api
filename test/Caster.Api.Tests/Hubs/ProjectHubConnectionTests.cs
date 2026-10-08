// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Hubs;
using Caster.Api.Tests.Support;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace Caster.Api.Tests.Hubs;

/// <summary>
/// <see cref="ProjectHub"/>'s own <c>[Authorize]</c>, the default policy (an authenticated user whose token
/// carries every scope in <c>Authorization:AuthorizationScope</c>), over a real SignalR connection to the
/// in-process server at <c>/hubs/project</c>, where <c>Startup.Configure</c> maps it.
/// </summary>
public class ProjectHubConnectionTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    private const string HubPath = "/hubs/project";

    /// <summary>A token scoped for another API only, the scope the default policy does not accept.</summary>
    private const string AnotherApisScope = "player";

    [Fact]
    public async Task A_member_holding_ViewProject_connects_over_WebSockets_and_joins_the_project()
    {
        var project = TestData.Project();
        await Seed(project);
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();
        await using var connection = Connection(actor);

        await connection.StartAsync(Ct);
        await connection.InvokeAsync(nameof(ProjectHub.JoinProject), project.Id, Ct);

        Assert.Equal(HubConnectionState.Connected, connection.State);
    }

    /// <summary>A request with no identity is answered with a 401; the anonymous client is the case under test.</summary>
    [Fact]
    public async Task Negotiate_without_an_identity_is_unauthorized()
    {
        await AssertStatus(HttpStatusCode.Unauthorized, await Client().PostAsync($"{HubPath}/negotiate?negotiateVersion=1", null, Ct));
    }

    /// <summary>
    /// The default policy refuses an actor whose token lacks the caster scope, whatever permissions it holds;
    /// <see cref="A_member_holding_ViewProject_connects_over_WebSockets_and_joins_the_project"/> is its control.
    /// </summary>
    [Fact]
    public async Task Negotiate_for_a_member_whose_token_lacks_the_caster_scope_is_forbidden()
    {
        var project = TestData.Project();
        await Seed(project);
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{HubPath}/negotiate?negotiateVersion=1");
        request.Headers.Add(TestAuthHandler.ScopeHeader, AnotherApisScope);

        await AssertStatus(HttpStatusCode.Forbidden, await Client(actor).SendAsync(request, Ct));
    }

    /// <summary>A WebSocket to the TestServer, carrying the headers every ApiTestBase client sends.</summary>
    /// <remarks>
    /// Under long polling a hub invocation runs outside any request, where no X-Test-Session header names the
    /// test's database; a WebSocket keeps the connection's own request, headers included, for every invocation.
    /// </remarks>
    private HubConnection Connection(TestActor actor)
    {
        var session = Client().DefaultRequestHeaders.GetValues(TestDatabaseScope.HeaderName).Single();

        return new HubConnectionBuilder()
            .WithUrl($"http://localhost{HubPath}", options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.WebSocketFactory = async (context, ct) =>
                {
                    var client = Factory.Server.CreateWebSocketClient();
                    client.ConfigureRequest = request =>
                    {
                        request.Headers[TestAuthHandler.UserHeader] = actor.Id.ToString();
                        request.Headers[TestAuthHandler.NameHeader] = actor.Name;
                        request.Headers[TestDatabaseScope.HeaderName] = session;
                    };

                    return await client.ConnectAsync(context.Uri, ct);
                };
            })
            .Build();
    }
}
