// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Hubs;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Tests.Support;

namespace Caster.Api.Tests.Hubs;

/// <summary>
/// <see cref="ProjectHub"/>'s join methods: each checks its permission through the real
/// <c>AuthorizationService</c> and its handlers, then adds the connection to the group the broadcasts go to,
/// and a refused join adds it to nothing.
/// </summary>
/// <remarks>
/// A hub method has no request, so the caller's claims come from <see cref="ClaimsPrincipalBuilder"/>, in
/// the shapes <c>TestActorTests</c> pins for the claims transformer's output.
/// </remarks>
public class ProjectHubTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task JoinProject_adds_a_member_holding_ViewProject_to_the_projects_group()
    {
        var project = TestData.Project();
        await Seed(project);
        var (hub, harness) = Hub(new ClaimsPrincipalBuilder().WithProject(project.Id, ProjectPermission.ViewProject));

        await hub.JoinProject(project.Id);

        await harness.Groups.Received(1).AddToGroupAsync(HubHarness.ConnectionId, project.Id.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinProject_refuses_a_caller_holding_ViewProject_only_on_another_project()
    {
        var project = TestData.Project();
        await Seed(project);
        var (hub, harness) = Hub(new ClaimsPrincipalBuilder().WithProject(Guid.NewGuid(), ProjectPermission.ViewProject));

        await Assert.ThrowsAsync<ForbiddenException>(() => hub.JoinProject(project.Id));

        await harness.Groups.DidNotReceive().AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinProject_adds_a_caller_holding_ViewProjects()
    {
        var project = TestData.Project();
        await Seed(project);
        var (hub, harness) = Hub(new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ViewProjects));

        await hub.JoinProject(project.Id);

        await harness.Groups.Received(1).AddToGroupAsync(HubHarness.ConnectionId, project.Id.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinProjectAdmin_adds_a_member_holding_ManageProject_to_the_projects_admin_group()
    {
        var project = TestData.Project();
        await Seed(project);
        var (hub, harness) = Hub(new ClaimsPrincipalBuilder().WithProject(project.Id, ProjectPermission.ManageProject));

        await hub.JoinProjectAdmin(project.Id);

        await harness.Groups.Received(1).AddToGroupAsync(
            HubHarness.ConnectionId, ProjectHubMethods.GetProjectAdminGroup(project.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinProjectAdmin_refuses_a_member_holding_only_EditProject()
    {
        var project = TestData.Project();
        await Seed(project);
        var (hub, harness) = Hub(new ClaimsPrincipalBuilder().WithProject(project.Id, ProjectPermission.EditProject));

        await Assert.ThrowsAsync<ForbiddenException>(() => hub.JoinProjectAdmin(project.Id));

        await harness.Groups.DidNotReceive().AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinGroup_adds_the_groups_manager_to_its_group()
    {
        var group = TestData.Group();
        await Seed(group);
        var (hub, harness) = Hub(new ClaimsPrincipalBuilder().WithGroup(group.Id, GroupPermission.ManageMembership));

        await hub.JoinGroup(group.Id);

        await harness.Groups.Received(1).AddToGroupAsync(HubHarness.ConnectionId, group.Id.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinGroup_refuses_a_caller_managing_only_another_group()
    {
        var group = TestData.Group();
        await Seed(group);
        var (hub, harness) = Hub(new ClaimsPrincipalBuilder().WithGroup(Guid.NewGuid(), GroupPermission.ManageMembership));

        await Assert.ThrowsAsync<ForbiddenException>(() => hub.JoinGroup(group.Id));

        await harness.Groups.DidNotReceive().AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinWorkspace_adds_a_member_holding_ViewProject_on_the_workspaces_project()
    {
        var (project, workspace, _) = await SeedWorkspaceWithDesign();
        var (hub, harness) = Hub(new ClaimsPrincipalBuilder().WithProject(project.Id, ProjectPermission.ViewProject));

        await hub.JoinWorkspace(workspace.Id);

        await harness.Groups.Received(1).AddToGroupAsync(HubHarness.ConnectionId, workspace.Id.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinWorkspace_refuses_a_member_holding_only_EditProject()
    {
        var (project, workspace, _) = await SeedWorkspaceWithDesign();
        var (hub, harness) = Hub(new ClaimsPrincipalBuilder().WithProject(project.Id, ProjectPermission.EditProject));

        await Assert.ThrowsAsync<ForbiddenException>(() => hub.JoinWorkspace(workspace.Id));

        await harness.Groups.DidNotReceive().AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinDesign_adds_a_member_holding_ViewProject_on_the_designs_project()
    {
        var (project, _, design) = await SeedWorkspaceWithDesign();
        var (hub, harness) = Hub(new ClaimsPrincipalBuilder().WithProject(project.Id, ProjectPermission.ViewProject));

        await hub.JoinDesign(design.Id);

        await harness.Groups.Received(1).AddToGroupAsync(HubHarness.ConnectionId, design.Id.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinDesign_refuses_a_caller_holding_ViewProject_only_on_another_project()
    {
        var (_, _, design) = await SeedWorkspaceWithDesign();
        var (hub, harness) = Hub(new ClaimsPrincipalBuilder().WithProject(Guid.NewGuid(), ProjectPermission.ViewProject));

        await Assert.ThrowsAsync<ForbiddenException>(() => hub.JoinDesign(design.Id));

        await harness.Groups.DidNotReceive().AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinWorkspacesAdmin_adds_a_caller_holding_ViewWorkspaces()
    {
        var (hub, harness) = Hub(new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ViewWorkspaces));

        await hub.JoinWorkspacesAdmin();

        await harness.Groups.Received(1).AddToGroupAsync(HubHarness.ConnectionId, nameof(HubGroups.WorkspacesAdmin), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinWorkspacesAdmin_refuses_a_caller_holding_only_ViewProjects()
    {
        var (hub, harness) = Hub(new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ViewProjects));

        await Assert.ThrowsAsync<ForbiddenException>(() => hub.JoinWorkspacesAdmin());

        await harness.Groups.DidNotReceive().AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinVlansAdmin_adds_a_caller_holding_ViewVLANs()
    {
        var (hub, harness) = Hub(new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ViewVLANs));

        await hub.JoinVlansAdmin();

        await harness.Groups.Received(1).AddToGroupAsync(HubHarness.ConnectionId, nameof(HubGroups.VlansAdmin), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinVlansAdmin_refuses_a_caller_holding_only_ManageVLANs()
    {
        var (hub, harness) = Hub(new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ManageVLANs));

        await Assert.ThrowsAsync<ForbiddenException>(() => hub.JoinVlansAdmin());

        await harness.Groups.DidNotReceiveWithAnyArgs().AddToGroupAsync(default, default, Ct);
    }

    [Fact]
    public async Task JoinRolesAdmin_adds_a_caller_holding_ViewRoles()
    {
        var (hub, harness) = Hub(new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ViewRoles));

        await hub.JoinRolesAdmin();

        await harness.Groups.Received(1).AddToGroupAsync(HubHarness.ConnectionId, nameof(HubGroups.RolesAdmin), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinRolesAdmin_refuses_a_caller_holding_only_ManageRoles()
    {
        var (hub, harness) = Hub(new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ManageRoles));

        await Assert.ThrowsAsync<ForbiddenException>(() => hub.JoinRolesAdmin());

        await harness.Groups.DidNotReceiveWithAnyArgs().AddToGroupAsync(default, default, Ct);
    }

    [Fact]
    public async Task LeaveProject_removes_the_connection_from_the_projects_group()
    {
        var projectId = Guid.NewGuid();
        var (hub, harness) = Hub(new ClaimsPrincipalBuilder());

        await hub.LeaveProject(projectId);

        await harness.Groups.Received(1).RemoveFromGroupAsync(HubHarness.ConnectionId, projectId.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPlanOutput_streams_the_stored_output_of_a_finished_plan_to_a_member_holding_ViewProject()
    {
        var (project, plan) = await SeedPlan();
        var (hub, _) = Hub(new ClaimsPrincipalBuilder().WithProject(project.Id, ProjectPermission.ViewProject));

        var output = await Collect(hub.GetPlanOutput(plan.Id, Ct));

        Assert.Equal([plan.Output], output);
    }

    [Fact]
    public async Task GetPlanOutput_refuses_a_caller_holding_ViewProject_only_on_another_project()
    {
        var (_, plan) = await SeedPlan();
        var (hub, _) = Hub(new ClaimsPrincipalBuilder().WithProject(Guid.NewGuid(), ProjectPermission.ViewProject));

        await Assert.ThrowsAsync<ForbiddenException>(() => Collect(hub.GetPlanOutput(plan.Id, Ct)));
    }

    [Fact]
    public async Task GetApplyOutput_streams_the_stored_output_to_a_caller_holding_ViewWorkspaces()
    {
        var (_, plan) = await SeedPlan();
        var apply = TestData.Apply(await RunOf(plan));
        await Seed(apply);
        var (hub, _) = Hub(new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ViewWorkspaces));

        var output = await Collect(hub.GetApplyOutput(apply.Id, Ct));

        Assert.Equal([apply.Output], output);
    }

    [Fact]
    public async Task GetApplyOutput_refuses_a_caller_holding_ViewProject_only_on_another_project()
    {
        var (_, plan) = await SeedPlan();
        var apply = TestData.Apply(await RunOf(plan));
        await Seed(apply);
        var (hub, _) = Hub(new ClaimsPrincipalBuilder().WithProject(Guid.NewGuid(), ProjectPermission.ViewProject));

        await Assert.ThrowsAsync<ForbiddenException>(() => Collect(hub.GetApplyOutput(apply.Id, Ct)));
    }

    [Fact]
    public async Task GetApplyOutput_refuses_a_caller_holding_only_ManageWorkspaces()
    {
        var (_, plan) = await SeedPlan();
        var apply = TestData.Apply(await RunOf(plan));
        await Seed(apply);
        var (hub, _) = Hub(new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ManageWorkspaces));

        await Assert.ThrowsAsync<ForbiddenException>(() => Collect(hub.GetApplyOutput(apply.Id, Ct)));
    }

    // ---- helpers --------------------------------------------------------------------------------

    private (ProjectHub Hub, HubHarness Harness) Hub(ClaimsPrincipalBuilder principal)
    {
        ClaimsPrincipal user = principal.Build();
        var harness = new HubHarness(principal.UserId, user);
        var hub = harness.Attach(new ProjectHub(
            new OutputService(),
            Db,
            AuthorizationHarness.CreateCasterAuthorizationService(Db, user)));

        return (hub, harness);
    }

    private static async Task<List<string>> Collect(IAsyncEnumerable<string> stream)
    {
        List<string> items = [];

        await foreach (var item in stream)
        {
            items.Add(item);
        }

        return items;
    }

    private async Task<(Project Project, Workspace Workspace, Design Design)> SeedWorkspaceWithDesign()
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        var workspace = TestData.Workspace(directory);
        var design = TestData.Design(directory);
        await Seed(project, directory, workspace, design);

        return (project, workspace, design);
    }

    private async Task<(Project Project, Plan Plan)> SeedPlan()
    {
        var (project, workspace, _) = await SeedWorkspaceWithDesign();
        var run = TestData.Run(workspace, RunStatus.Planned);
        var plan = TestData.Plan(run);
        await Seed(run, plan);

        return (project, plan);
    }

    private async Task<Run> RunOf(Plan plan) => await Db.Runs.FindAsync([plan.RunId], Ct);
}
