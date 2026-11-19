// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Hubs;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Tests.Support;
using Microsoft.AspNetCore.SignalR;

namespace Caster.Api.Tests.Hubs;

/// <summary>
/// <see cref="ProjectHub"/>'s project presence: joining a project tells the others in it who arrived and
/// answers with who is already there; leaving, or disconnecting, tells them who departed, once a user's
/// last connection to the project has gone.
/// </summary>
/// <remarks>
/// Each connection is a hub over a harness of its own, all sharing one tracker, which is how SignalR runs
/// them: a hub per invocation, one singleton tracker. Each harness's proxies hold what its own connection
/// sent.
/// </remarks>
public class ProjectHubPresenceTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    private readonly ProjectPresenceTracker _tracker = new();

    [Fact]
    public async Task JoinProject_tells_the_others_in_the_project_who_arrived()
    {
        var project = await SeedProject();
        var alice = Connect(Member(project, "Alice"));

        await alice.Hub.JoinProject(project.Id);

        var arrived = HubHarness.Sent<PresentUser>(alice.Others(project.Id), ProjectHubMethods.PresenceArrived);
        Assert.Equal(new PresentUser(alice.Harness.UserId, "Alice"), arrived);
        alice.Harness.AssertAddressedOnly(nameof(IHubCallerClients.OthersInGroup));
    }

    [Fact]
    public async Task JoinProject_answers_with_the_others_already_present_but_not_the_caller()
    {
        var project = await SeedProject();
        var alice = Connect(Member(project, "Alice"));
        var bob = Connect(Member(project, "Bob"));
        await alice.Hub.JoinProject(project.Id);

        var present = await bob.Hub.JoinProject(project.Id);

        Assert.Equal([new PresentUser(alice.Harness.UserId, "Alice")], present);
    }

    [Fact]
    public async Task JoinProject_from_a_second_tab_announces_nothing_and_lists_only_the_others()
    {
        var project = await SeedProject();
        var alice = Member(project, "Alice");
        var firstTab = Connect(alice, "alice-tab-1");
        var secondTab = Connect(alice, "alice-tab-2");
        await firstTab.Hub.JoinProject(project.Id);

        var present = await secondTab.Hub.JoinProject(project.Id);

        Assert.Empty(present);
        HubHarness.NothingSent(secondTab.Others(project.Id), ProjectHubMethods.PresenceArrived);
    }

    [Fact]
    public async Task JoinProject_again_on_the_same_connection_announces_nothing()
    {
        var project = await SeedProject();
        var alice = Connect(Member(project, "Alice"));
        await alice.Hub.JoinProject(project.Id);

        await alice.Hub.JoinProject(project.Id);

        // Sent<> fails unless it was sent exactly once, the first join's.
        HubHarness.Sent<PresentUser>(alice.Others(project.Id), ProjectHubMethods.PresenceArrived);
    }

    [Fact]
    public async Task JoinProject_refused_adds_the_caller_to_no_ones_presence()
    {
        var project = await SeedProject();
        var outsider = Connect(new ClaimsPrincipalBuilder().WithName("Mallory").WithProject(Guid.NewGuid(), ProjectPermission.ViewProject));
        var bob = Connect(Member(project, "Bob"));

        await Assert.ThrowsAsync<ForbiddenException>(() => outsider.Hub.JoinProject(project.Id));
        var present = await bob.Hub.JoinProject(project.Id);

        Assert.Empty(present);
        outsider.Harness.NothingAddressed();
    }

    [Fact]
    public async Task JoinProject_names_the_caller_from_their_user_row_when_the_token_has_no_name()
    {
        var project = await SeedProject();
        var principal = Member(project, "");
        await Seed(TestData.User(principal.UserId, name: "Stored Name"));
        var caller = Connect(principal);

        await caller.Hub.JoinProject(project.Id);

        var arrived = HubHarness.Sent<PresentUser>(caller.Others(project.Id), ProjectHubMethods.PresenceArrived);
        Assert.Equal("Stored Name", arrived.Name);
    }

    [Fact]
    public async Task LeaveProject_from_the_last_tab_tells_the_project_who_departed()
    {
        var project = await SeedProject();
        var alice = Connect(Member(project, "Alice"));
        await alice.Hub.JoinProject(project.Id);

        await alice.Hub.LeaveProject(project.Id);

        var departed = HubHarness.Sent<PresentUser>(alice.Harness.Group(project.Id.ToString()), ProjectHubMethods.PresenceDeparted);
        Assert.Equal(new PresentUser(alice.Harness.UserId, "Alice"), departed);
        Assert.Empty(await Connect(Member(project, "Bob")).Hub.JoinProject(project.Id));
    }

    [Fact]
    public async Task LeaveProject_from_one_of_two_tabs_announces_nothing_and_keeps_the_user_present()
    {
        var project = await SeedProject();
        var alice = Member(project, "Alice");
        var firstTab = Connect(alice, "alice-tab-1");
        var secondTab = Connect(alice, "alice-tab-2");
        await firstTab.Hub.JoinProject(project.Id);
        await secondTab.Hub.JoinProject(project.Id);

        await firstTab.Hub.LeaveProject(project.Id);

        HubHarness.NothingSent(firstTab.Harness.Group(project.Id.ToString()), ProjectHubMethods.PresenceDeparted);
        var present = await Connect(Member(project, "Bob")).Hub.JoinProject(project.Id);
        Assert.Equal([new PresentUser(alice.UserId, "Alice")], present);
    }

    [Fact]
    public async Task LeaveProject_without_having_joined_announces_nothing()
    {
        var project = await SeedProject();
        var alice = Connect(Member(project, "Alice"));

        await alice.Hub.LeaveProject(project.Id);

        alice.Harness.NothingAddressed();
    }

    [Fact]
    public async Task Disconnecting_tells_each_project_the_connection_was_the_users_last_one_to()
    {
        var first = await SeedProject();
        var second = await SeedProject();
        var principal = new ClaimsPrincipalBuilder()
            .WithName("Alice")
            .WithProject(first.Id, ProjectPermission.ViewProject)
            .WithProject(second.Id, ProjectPermission.ViewProject);
        var tab = Connect(principal, "alice-tab-1");
        var otherTab = Connect(principal, "alice-tab-2");
        await tab.Hub.JoinProject(first.Id);
        await tab.Hub.JoinProject(second.Id);
        await otherTab.Hub.JoinProject(second.Id);

        await tab.Hub.OnDisconnectedAsync(null);

        var departed = HubHarness.Sent<PresentUser>(tab.Harness.Group(first.Id.ToString()), ProjectHubMethods.PresenceDeparted);
        Assert.Equal(new PresentUser(principal.UserId, "Alice"), departed);
        HubHarness.NothingSent(tab.Harness.Group(second.Id.ToString()), ProjectHubMethods.PresenceDeparted);
        Assert.Equal([new PresentUser(principal.UserId, "Alice")], _tracker.GetUsers(second.Id));
        Assert.Empty(_tracker.GetUsers(first.Id));
    }

    [Fact]
    public async Task Disconnecting_without_having_joined_announces_nothing()
    {
        var alice = Connect(new ClaimsPrincipalBuilder().WithName("Alice"));

        await alice.Hub.OnDisconnectedAsync(null);

        alice.Harness.NothingAddressed();
    }

    /// <summary>
    /// Verifies: a join that was still waiting when the connection closed leaves no one registered, so the
    /// others never see a user who is gone.
    /// Interacts with: HubHarness (Groups.AddToGroupAsync aborts the connection as the join waits on it), ProjectPresenceTracker.
    /// Data: Alice's connection aborts after the join is authorized and before it registers; Bob then joins.
    /// </summary>
    [Fact]
    public async Task JoinProject_on_a_connection_that_closed_meanwhile_leaves_no_one_present()
    {
        var project = await SeedProject();
        var alice = Connect(Member(project, "Alice"));
        var bob = Connect(Member(project, "Bob"));
        using var aborted = new CancellationTokenSource();
        alice.Harness.Context.ConnectionAborted.Returns(aborted.Token);
        alice.Harness.Groups
            .AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                aborted.Cancel();
                return Task.CompletedTask;
            });

        var answer = await alice.Hub.JoinProject(project.Id);
        var present = await bob.Hub.JoinProject(project.Id);

        Assert.Empty(answer);
        Assert.Empty(present);
        HubHarness.NothingSent(alice.Others(project.Id), ProjectHubMethods.PresenceArrived);
    }

    /// <summary>
    /// Verifies: a user's departure is announced before their next arrival, however slowly it is sent.
    /// Interacts with: ProjectPresenceTracker.LockProjectAsync.
    /// Data: Alice's only tab leaves and its departure broadcast is held up; her second tab joins meanwhile.
    /// </summary>
    [Fact]
    public async Task A_departure_is_announced_before_the_same_users_next_arrival()
    {
        var project = await SeedProject();
        var alice = Member(project, "Alice");
        var firstTab = Connect(alice, "alice-tab-1");
        var secondTab = Connect(alice, "alice-tab-2");
        await firstTab.Hub.JoinProject(project.Id);

        var sent = new List<string>();
        var release = new TaskCompletionSource();
        firstTab.Harness.Group(project.Id.ToString())
            .SendCoreAsync(ProjectHubMethods.PresenceDeparted, Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await release.Task;
                lock (sent) sent.Add("departed");
            });
        secondTab.OthersByGroup[project.Id.ToString()] = Substitute.For<IClientProxy>();
        secondTab.OthersByGroup[project.Id.ToString()]
            .SendCoreAsync(ProjectHubMethods.PresenceArrived, Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                lock (sent) sent.Add("arrived");
                return Task.CompletedTask;
            });

        var leaving = firstTab.Hub.LeaveProject(project.Id);
        var joining = secondTab.Hub.JoinProject(project.Id);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        release.SetResult();
        await Task.WhenAll(leaving, joining);

        Assert.Equal(["departed", "arrived"], sent);
    }

    // ---- helpers --------------------------------------------------------------------------------

    private sealed record Connection(ProjectHub Hub, HubHarness Harness, Dictionary<string, IClientProxy> OthersByGroup)
    {
        /// <summary>The proxy for <c>Clients.OthersInGroup(projectId)</c>, which the harness leaves unset.</summary>
        public IClientProxy Others(Guid projectId) => OthersByGroup.TryGetValue(projectId.ToString(), out var proxy)
            ? proxy
            : Substitute.For<IClientProxy>();
    }

    private async Task<Project> SeedProject()
    {
        var project = TestData.Project();
        await Seed(project);
        return project;
    }

    private static ClaimsPrincipalBuilder Member(Project project, string name) =>
        new ClaimsPrincipalBuilder().WithName(name).WithProject(project.Id, ProjectPermission.ViewProject);

    private Connection Connect(ClaimsPrincipalBuilder principal, string connectionId = HubHarness.ConnectionId)
    {
        ClaimsPrincipal user = principal.Build();
        var harness = new HubHarness(principal.UserId, user, connectionId);
        var others = new Dictionary<string, IClientProxy>();
        harness.Clients.OthersInGroup(Arg.Any<string>()).Returns(call =>
        {
            var group = call.Arg<string>();

            if (!others.TryGetValue(group, out var proxy))
            {
                proxy = Substitute.For<IClientProxy>();
                others[group] = proxy;
            }

            return proxy;
        });

        var hub = harness.Attach(new ProjectHub(
            new OutputService(),
            Db,
            AuthorizationHarness.CreateCasterAuthorizationService(Db, user),
            _tracker));

        return new Connection(hub, harness, others);
    }
}
