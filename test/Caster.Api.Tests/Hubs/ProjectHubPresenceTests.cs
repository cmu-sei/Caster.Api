// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Hubs;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Tests.Support;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;

namespace Caster.Api.Tests.Hubs;

/// <summary>
/// <see cref="ProjectHub"/>'s project presence: joining or leaving a project, or disconnecting, tells the
/// project's group who is there now, as a state with a version, once the set of users has changed; joining
/// also answers with that state.
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
    public async Task JoinProject_tells_the_group_who_is_there_and_answers_with_the_same_state()
    {
        var project = await SeedProject();
        var alice = Connect(Member(project, "Alice"));

        var answer = await alice.Hub.JoinProject(project.Id);

        var sent = HubHarness.Sent<PresenceState>(alice.Harness.Group(project.Id.ToString()), ProjectHubMethods.PresenceChanged);
        Assert.Equal(answer.Version, sent.Version);
        Assert.Equal(answer.Users, sent.Users);
        Assert.Equal(project.Id, answer.ProjectId);
        Assert.Equal([new PresentUser(alice.Harness.UserId, "Alice")], answer.Users);
    }

    [Fact]
    public async Task JoinProject_answers_with_everyone_present_the_caller_included()
    {
        var project = await SeedProject();
        var alice = Connect(Member(project, "Alice"));
        var bob = Connect(Member(project, "Bob"));
        await alice.Hub.JoinProject(project.Id);

        var answer = await bob.Hub.JoinProject(project.Id);

        Assert.Equal(
            [new PresentUser(alice.Harness.UserId, "Alice"), new PresentUser(bob.Harness.UserId, "Bob")],
            answer.Users);
    }

    [Fact]
    public async Task Each_change_is_sent_with_a_higher_version()
    {
        var project = await SeedProject();
        var alice = Connect(Member(project, "Alice"));
        var bob = Connect(Member(project, "Bob"));

        var first = await alice.Hub.JoinProject(project.Id);
        var second = await bob.Hub.JoinProject(project.Id);
        await bob.Hub.LeaveProject(project.Id);

        var left = (await Connect(Member(project, "Carol")).Hub.JoinProject(project.Id)).Version;
        Assert.True(second.Version > first.Version);
        Assert.True(left > second.Version);
    }

    [Fact]
    public async Task JoinProject_from_a_second_tab_sends_nothing_and_lists_the_user_once()
    {
        var project = await SeedProject();
        var alice = Member(project, "Alice");
        var firstTab = Connect(alice, "alice-tab-1");
        var secondTab = Connect(alice, "alice-tab-2");
        var first = await firstTab.Hub.JoinProject(project.Id);

        var answer = await secondTab.Hub.JoinProject(project.Id);

        Assert.Equal(first.Version, answer.Version);
        Assert.Equal(first.Users, answer.Users);
        HubHarness.NothingSent(secondTab.Harness.Group(project.Id.ToString()), ProjectHubMethods.PresenceChanged);
    }

    [Fact]
    public async Task JoinProject_again_on_the_same_connection_sends_nothing()
    {
        var project = await SeedProject();
        var alice = Connect(Member(project, "Alice"));
        await alice.Hub.JoinProject(project.Id);

        await alice.Hub.JoinProject(project.Id);

        // Sent<> fails unless it was sent exactly once, the first join's.
        HubHarness.Sent<PresenceState>(alice.Harness.Group(project.Id.ToString()), ProjectHubMethods.PresenceChanged);
    }

    [Fact]
    public async Task JoinProject_refused_adds_the_caller_to_no_ones_presence()
    {
        var project = await SeedProject();
        var outsider = Connect(new ClaimsPrincipalBuilder().WithName("Mallory").WithProject(Guid.NewGuid(), ProjectPermission.ViewProject));
        var bob = Connect(Member(project, "Bob"));

        await Assert.ThrowsAsync<ForbiddenException>(() => outsider.Hub.JoinProject(project.Id));
        var answer = await bob.Hub.JoinProject(project.Id);

        Assert.Equal([new PresentUser(bob.Harness.UserId, "Bob")], answer.Users);
        outsider.Harness.NothingAddressed();
    }

    [Fact]
    public async Task JoinProject_names_the_caller_from_their_user_row_when_the_token_has_no_name()
    {
        var project = await SeedProject();
        var principal = Member(project, "");
        await Seed(TestData.User(principal.UserId, name: "Stored Name"));
        var caller = Connect(principal);

        var answer = await caller.Hub.JoinProject(project.Id);

        Assert.Equal("Stored Name", Assert.Single(answer.Users).Name);
    }

    [Fact]
    public async Task LeaveProject_from_the_last_tab_tells_the_group_who_is_left()
    {
        var project = await SeedProject();
        var alice = Connect(Member(project, "Alice"));
        var bob = Connect(Member(project, "Bob"));
        await alice.Hub.JoinProject(project.Id);
        await bob.Hub.JoinProject(project.Id);
        var group = alice.Harness.Group(project.Id.ToString());

        await alice.Hub.LeaveProject(project.Id);

        var sent = Sent(group);
        Assert.Equal([new PresentUser(bob.Harness.UserId, "Bob")], sent.Last().Users);
    }

    [Fact]
    public async Task LeaveProject_from_one_of_two_tabs_sends_nothing_and_keeps_the_user_present()
    {
        var project = await SeedProject();
        var alice = Member(project, "Alice");
        var firstTab = Connect(alice, "alice-tab-1");
        var secondTab = Connect(alice, "alice-tab-2");
        await firstTab.Hub.JoinProject(project.Id);
        await secondTab.Hub.JoinProject(project.Id);

        await firstTab.Hub.LeaveProject(project.Id);

        // Sent<> fails unless it was sent exactly once, the first join's.
        HubHarness.Sent<PresenceState>(firstTab.Harness.Group(project.Id.ToString()), ProjectHubMethods.PresenceChanged);
        var answer = await Connect(Member(project, "Bob")).Hub.JoinProject(project.Id);
        Assert.Contains(new PresentUser(alice.UserId, "Alice"), answer.Users);
    }

    [Fact]
    public async Task LeaveProject_without_having_joined_sends_nothing()
    {
        var project = await SeedProject();
        var alice = Connect(Member(project, "Alice"));

        await alice.Hub.LeaveProject(project.Id);

        alice.Harness.AssertAddressedOnly();
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

        Assert.Empty(Sent(tab.Harness.Group(first.Id.ToString())).Last().Users);
        // Only the join announced her to the second project: the other tab kept her there.
        Assert.Single(Sent(tab.Harness.Group(second.Id.ToString())));
        Assert.Empty(_tracker.ProjectsOf("alice-tab-1"));
    }

    [Fact]
    public async Task Disconnecting_without_having_joined_sends_nothing()
    {
        var alice = Connect(new ClaimsPrincipalBuilder().WithName("Alice"));

        await alice.Hub.OnDisconnectedAsync(null);

        alice.Harness.NothingAddressed();
    }

    /// <summary>
    /// Verifies: a send that fails on disconnect doesn't leave the connection listed or stop the sends
    /// for its other projects.
    /// Data: Alice has two projects open; the send for the first throws.
    /// </summary>
    [Fact]
    public async Task Disconnecting_forgets_the_connection_and_goes_on_when_a_send_fails()
    {
        var first = await SeedProject();
        var second = await SeedProject();
        var principal = new ClaimsPrincipalBuilder()
            .WithName("Alice")
            .WithProject(first.Id, ProjectPermission.ViewProject)
            .WithProject(second.Id, ProjectPermission.ViewProject);
        var tab = Connect(principal, "alice-tab-1");
        await tab.Hub.JoinProject(first.Id);
        await tab.Hub.JoinProject(second.Id);
        tab.Harness.Group(first.Id.ToString())
            .SendCoreAsync(ProjectHubMethods.PresenceChanged, Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("send failed"));
        tab.Harness.Group(second.Id.ToString()).ClearReceivedCalls();

        await tab.Hub.OnDisconnectedAsync(null);

        Assert.Empty(_tracker.ProjectsOf("alice-tab-1"));
        Assert.Empty(Sent(tab.Harness.Group(second.Id.ToString())).Last().Users);
    }

    /// <summary>
    /// Verifies: a join whose send to the group fails still registers the caller and answers with the
    /// state, rather than failing the caller.
    /// Data: Alice joins; the send to the project's group throws.
    /// </summary>
    [Fact]
    public async Task JoinProject_answers_when_the_send_fails()
    {
        var project = await SeedProject();
        var alice = Connect(Member(project, "Alice"), "alice-tab-1");
        FailSends(alice, project.Id);

        var answer = await alice.Hub.JoinProject(project.Id);

        Assert.Equal([new PresentUser(alice.Harness.UserId, "Alice")], answer.Users);
        Assert.Equal([project.Id], _tracker.ProjectsOf("alice-tab-1"));
    }

    /// <summary>
    /// Verifies: a leave whose send to the group fails still forgets the connection, without failing the
    /// caller.
    /// Data: Alice has the project open; the send to the project's group throws when she leaves.
    /// </summary>
    [Fact]
    public async Task LeaveProject_forgets_the_connection_when_the_send_fails()
    {
        var project = await SeedProject();
        var alice = Connect(Member(project, "Alice"), "alice-tab-1");
        await alice.Hub.JoinProject(project.Id);
        FailSends(alice, project.Id);

        await alice.Hub.LeaveProject(project.Id);

        Assert.Empty(_tracker.ProjectsOf("alice-tab-1"));
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
        var bobs = await bob.Hub.JoinProject(project.Id);

        Assert.Empty(answer.Users);
        Assert.Equal([new PresentUser(bob.Harness.UserId, "Bob")], bobs.Users);
    }

    /// <summary>
    /// Verifies: a repeat join that finds its connection closed removes the registration it already had,
    /// and the disconnect that follows has nothing more to send.
    /// Data: Alice joins, joins again as the connection aborts, then the disconnect runs; Bob is in the project.
    /// </summary>
    [Fact]
    public async Task A_repeat_join_on_a_closed_connection_removes_the_registration_once()
    {
        var project = await SeedProject();
        var alice = Connect(Member(project, "Alice"));
        var bob = Connect(Member(project, "Bob"));
        await alice.Hub.JoinProject(project.Id);
        using var aborted = new CancellationTokenSource();
        alice.Harness.Context.ConnectionAborted.Returns(aborted.Token);
        alice.Harness.Groups
            .AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                aborted.Cancel();
                return Task.CompletedTask;
            });

        await alice.Hub.JoinProject(project.Id);
        var present = await bob.Hub.JoinProject(project.Id);
        var sentBefore = Sent(alice.Harness.Group(project.Id.ToString())).Count;
        await alice.Hub.OnDisconnectedAsync(null);

        Assert.Equal([new PresentUser(bob.Harness.UserId, "Bob")], present.Users);
        Assert.Equal(sentBefore, Sent(alice.Harness.Group(project.Id.ToString())).Count);
    }

    /// <summary>
    /// Verifies: leaving a project the connection never had open sends nothing.
    /// Data: a connection that never joined leaves a project id it made up.
    /// </summary>
    [Fact]
    public async Task LeaveProject_for_a_project_the_connection_never_joined_sends_nothing()
    {
        var alice = Connect(new ClaimsPrincipalBuilder().WithName("Alice"));

        await alice.Hub.LeaveProject(Guid.NewGuid());

        alice.Harness.NothingAddressed();
    }

    // ---- helpers --------------------------------------------------------------------------------

    private sealed record Connection(ProjectHub Hub, HubHarness Harness);

    /// <summary>Every presence state sent to <paramref name="group"/>, oldest first.</summary>
    private static IReadOnlyList<PresenceState> Sent(IClientProxy group) =>
        [.. group.ReceivedCalls()
            .Where(x => x.GetMethodInfo().Name == nameof(IClientProxy.SendCoreAsync))
            .Select(x => x.GetArguments())
            .Where(x => (string)x[0] == ProjectHubMethods.PresenceChanged)
            .Select(x => (PresenceState)((object[])x[1])[0])];

    /// <summary>Makes every presence send to <paramref name="projectId"/>'s group from <paramref name="connection"/> throw.</summary>
    private static void FailSends(Connection connection, Guid projectId) =>
        connection.Harness.Group(projectId.ToString())
            .SendCoreAsync(ProjectHubMethods.PresenceChanged, Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("send failed"));

    private async Task<Project> SeedProject()
    {
        var project = TestData.Project();
        await Seed(project);
        return project;
    }

    private static ClaimsPrincipalBuilder Member(Project project, string name) =>
        new ClaimsPrincipalBuilder().WithName(name).WithProject(project.Id, ProjectPermission.ViewProject);

    private Connection Connect(ClaimsPrincipalBuilder principal, string connectionId = null)
    {
        ClaimsPrincipal user = principal.Build();
        var harness = new HubHarness(principal.UserId, user, connectionId ?? Guid.NewGuid().ToString());

        var hub = harness.Attach(new ProjectHub(
            new OutputService(),
            Db,
            AuthorizationHarness.CreateCasterAuthorizationService(Db, user),
            _tracker,
            NullLogger<ProjectHub>.Instance));

        return new Connection(hub, harness);
    }
}
