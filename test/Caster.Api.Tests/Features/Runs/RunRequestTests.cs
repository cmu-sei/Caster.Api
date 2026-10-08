// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Microsoft.Extensions.DependencyInjection;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Directory = Caster.Api.Domain.Models.Directory;
using Run = Caster.Api.Domain.Models.Run;
using RunView = Caster.Api.Features.Runs.Run;

namespace Caster.Api.Tests.Features.Runs;

/// <summary>
/// <c>RunsController</c>: a workspace's runs are read with ViewProject (or ViewProjects) and created,
/// rejected, cancelled and state-saved with EditProject (or EditProjects); the cross-workspace lists need
/// system permissions. The run queue is registered but not started, so a created run is queued and never
/// planned.
/// </summary>
public class RunRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    // ---- reads ----------------------------------------------------------------------------------

    [Fact]
    public async Task Get_returns_the_run_with_its_plan_to_a_member_holding_ViewProject()
    {
        var (project, _, run) = await SeedRun(RunStatus.Planned);
        var plan = TestData.Plan(run);
        await Seed(plan);
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/runs/{run.Id}?includePlan=true", Ct);

        Assert.Equal(plan.Id, (await ReadAsync<RunView>(response)).Plan.Id);
    }

    [Fact]
    public async Task Get_returns_the_run_to_a_caller_holding_ViewProjects()
    {
        var (_, _, run) = await SeedRun();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).GetAsync($"api/runs/{run.Id}", Ct));
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var (project, _, run) = await SeedRun();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/runs/{run.Id}", Ct));
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_ViewProject_only_on_another_project()
    {
        var (_, _, run) = await SeedRun();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/runs/{run.Id}", Ct));
    }

    [Fact]
    public async Task GetByWorkspace_lists_the_runs_for_a_member_holding_ViewProject()
    {
        var (project, workspace, run) = await SeedRun();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/workspaces/{workspace.Id}/runs", Ct);

        Assert.Equal([run.Id], (await ReadAsync<RunView[]>(response)).Select(x => x.Id));
    }

    [Fact]
    public async Task GetByWorkspace_lists_the_runs_for_a_caller_holding_ViewProjects()
    {
        var (_, workspace, _) = await SeedRun();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).GetAsync($"api/workspaces/{workspace.Id}/runs", Ct));
    }

    [Fact]
    public async Task GetByWorkspace_is_forbidden_for_a_caller_holding_ViewProject_only_on_another_project()
    {
        var (_, workspace, _) = await SeedRun();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/workspaces/{workspace.Id}/runs", Ct));
    }

    [Fact]
    public async Task GetByWorkspace_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var (project, workspace, _) = await SeedRun();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/workspaces/{workspace.Id}/runs", Ct));
    }

    [Fact]
    public async Task GetAll_lists_the_runs_for_a_caller_holding_ViewWorkspaces()
    {
        var (_, _, run) = await SeedRun();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewWorkspaces).SeedAsync();

        var response = await Client(actor).GetAsync("api/runs", Ct);

        Assert.Equal([run.Id], (await ReadAsync<RunView[]>(response)).Select(x => x.Id));
    }

    [Fact]
    public async Task GetAll_lists_the_runs_for_a_caller_holding_ViewProjects()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).GetAsync("api/runs", Ct));
    }

    [Fact]
    public async Task GetAll_is_forbidden_for_a_member_holding_ViewProject_on_a_project()
    {
        var (project, _, _) = await SeedRun();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync("api/runs", Ct));
    }

    [Fact]
    public async Task GetAll_is_forbidden_for_a_caller_holding_only_ManageWorkspaces()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageWorkspaces).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync("api/runs", Ct));
    }

    // ---- POST api/runs --------------------------------------------------------------------------

    [Fact]
    public async Task Create_by_a_member_holding_EditProject_stores_a_queued_run_and_queues_it()
    {
        var (project, _, workspace) = await SeedWorkspace();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/runs", new { workspaceId = workspace.Id }, Ct);

        var created = await ReadAsync<RunView>(response);
        var stored = await StoredRun(created.Id);
        Assert.Equal(RunStatus.Queued, stored.Status);
        Assert.Equal(actor.Id, stored.CreatedById);
        var queue = Factory.Services.GetRequiredService<IRunQueueService>();
        Assert.Single(queue.GetQueuePositions(), x => x.RunId == created.Id && x.WorkspaceId == workspace.Id);
    }

    [Fact]
    public async Task Create_by_a_caller_holding_EditProjects_stores_a_run()
    {
        var (_, _, workspace) = await SeedWorkspace();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.Created, await Client(actor).PostAsJsonAsync("api/runs", new { workspaceId = workspace.Id }, Ct));
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, _, workspace) = await SeedWorkspace();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/runs", new { workspaceId = workspace.Id }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.False(await AnyRun(workspace.Id));
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, _, workspace) = await SeedWorkspace();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/runs", new { workspaceId = workspace.Id }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.False(await AnyRun(workspace.Id));
    }

    [Fact]
    public async Task Create_while_the_workspace_has_a_run_in_progress_is_a_conflict()
    {
        var (_, workspace, _) = await SeedRun();

        var response = await RootClient.PostAsJsonAsync("api/runs", new { workspaceId = workspace.Id }, Ct);

        await AssertProblem(HttpStatusCode.Conflict, response);
    }

    // ---- reject, cancel, save-state -------------------------------------------------------------

    [Fact]
    public async Task Reject_by_a_member_holding_EditProject_rejects_the_run_and_its_plan()
    {
        var (project, _, run) = await SeedPlannedRun();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsync($"api/runs/{run.Id}/actions/reject", null, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        await using var context = NewContext();
        var stored = await context.Runs.Include(x => x.Plan).SingleAsync(x => x.Id == run.Id, Ct);
        Assert.Equal(RunStatus.Rejected, stored.Status);
        Assert.Equal(PlanStatus.Rejected, stored.Plan.Status);
    }

    [Fact]
    public async Task Reject_by_a_caller_holding_EditProjects_rejects_the_run()
    {
        var (_, _, run) = await SeedPlannedRun();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        await Client(actor).PostAsync($"api/runs/{run.Id}/actions/reject", null, Ct);

        Assert.Equal(RunStatus.Rejected, (await StoredRun(run.Id)).Status);
    }

    [Fact]
    public async Task Reject_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, _, run) = await SeedPlannedRun();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PostAsync($"api/runs/{run.Id}/actions/reject", null, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(RunStatus.Planned, (await StoredRun(run.Id)).Status);
    }

    [Fact]
    public async Task Reject_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, _, run) = await SeedPlannedRun();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).PostAsync($"api/runs/{run.Id}/actions/reject", null, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(RunStatus.Planned, (await StoredRun(run.Id)).Status);
    }

    /// <summary>Rejecting a run whose plan is still queued is answered with a 500.</summary>
    [Fact]
    public async Task Reject_of_a_run_whose_plan_is_still_queued_answers_with_a_server_error()
    {
        var (_, _, run) = await SeedRun();
        await Seed(TestData.Plan(run, PlanStatus.Queued));

        var response = await RootClient.PostAsync($"api/runs/{run.Id}/actions/reject", null, Ct);

        var problem = await AssertProblem(HttpStatusCode.InternalServerError, response);

        Assert.Equal("Cannot reject a Run with a Plan in progress. Please try again when it has completed.", problem.Detail);
    }

    [Fact]
    public async Task Cancel_by_a_member_holding_EditProject_rejects_a_queued_run()
    {
        var (project, _, run) = await SeedRun();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/runs/{run.Id}/actions/cancel", new { force = false }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(RunStatus.Rejected, (await StoredRun(run.Id)).Status);
    }

    [Fact]
    public async Task Cancel_by_a_caller_holding_EditProjects_rejects_a_queued_run()
    {
        var (_, _, run) = await SeedRun();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        await Client(actor).PostAsJsonAsync($"api/runs/{run.Id}/actions/cancel", new { force = false }, Ct);

        Assert.Equal(RunStatus.Rejected, (await StoredRun(run.Id)).Status);
    }

    [Fact]
    public async Task Cancel_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, _, run) = await SeedRun();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/runs/{run.Id}/actions/cancel", new { force = false }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(RunStatus.Queued, (await StoredRun(run.Id)).Status);
    }

    [Fact]
    public async Task Cancel_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, _, run) = await SeedRun();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/runs/{run.Id}/actions/cancel", new { force = false }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(RunStatus.Queued, (await StoredRun(run.Id)).Status);
    }

    /// <summary>Cancelling a run that has finished is answered with a 500.</summary>
    [Fact]
    public async Task Cancel_of_a_finished_run_answers_with_a_server_error()
    {
        var (_, _, run) = await SeedRun(RunStatus.Applied);

        var response = await RootClient.PostAsJsonAsync($"api/runs/{run.Id}/actions/cancel", new { force = false }, Ct);

        var problem = await AssertProblem(HttpStatusCode.InternalServerError, response);

        Assert.Equal("Cannot cancel a Run that is not queued or in progress", problem.Detail);
    }

    [Fact]
    public async Task SaveState_by_a_member_holding_EditProject_is_accepted_for_a_run_in_state_error()
    {
        var (project, _, run) = await SeedRun(RunStatus.Applied_StateError);
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsync($"api/runs/{run.Id}/actions/save-state", null, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
    }

    [Fact]
    public async Task SaveState_by_a_caller_holding_EditProjects_is_accepted()
    {
        var (_, _, run) = await SeedRun(RunStatus.Applied_StateError);
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).PostAsync($"api/runs/{run.Id}/actions/save-state", null, Ct));
    }

    [Fact]
    public async Task SaveState_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, _, run) = await SeedRun(RunStatus.Applied_StateError);
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsync($"api/runs/{run.Id}/actions/save-state", null, Ct));
    }

    [Fact]
    public async Task SaveState_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, _, run) = await SeedRun(RunStatus.Applied_StateError);
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsync($"api/runs/{run.Id}/actions/save-state", null, Ct));
    }

    // ---- the queue ------------------------------------------------------------------------------

    [Fact]
    public async Task GetQueuePosition_is_answered_for_a_member_holding_ViewProject()
    {
        var (project, _, run) = await SeedRun();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        await AssertStatus(HttpStatusCode.NoContent, await Client(actor).GetAsync($"api/runs/{run.Id}/queue-position", Ct));
    }

    [Fact]
    public async Task GetQueuePosition_is_answered_for_a_caller_holding_ViewWorkspaces()
    {
        var (_, _, run) = await SeedRun();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewWorkspaces).SeedAsync();

        await AssertStatus(HttpStatusCode.NoContent, await Client(actor).GetAsync($"api/runs/{run.Id}/queue-position", Ct));
    }

    [Fact]
    public async Task GetQueuePosition_is_forbidden_for_a_caller_holding_ViewProject_only_on_another_project()
    {
        var (_, _, run) = await SeedRun();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/runs/{run.Id}/queue-position", Ct));
    }

    [Fact]
    public async Task GetQueuePositions_is_answered_for_a_caller_holding_ViewWorkspaces()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewWorkspaces).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).GetAsync("api/runs/queue", Ct));
    }

    [Fact]
    public async Task GetQueuePositions_is_forbidden_for_a_caller_holding_only_ViewProjects()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync("api/runs/queue", Ct));
    }

    // ---- helpers --------------------------------------------------------------------------------

    private async Task<(Project Project, Directory Directory, Workspace Workspace)> SeedWorkspace()
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        var workspace = TestData.Workspace(directory);
        await Seed(project, directory, workspace);

        return (project, directory, workspace);
    }

    private async Task<(Project Project, Workspace Workspace, Run Run)> SeedRun(RunStatus status = RunStatus.Queued)
    {
        var (project, _, workspace) = await SeedWorkspace();
        var run = TestData.Run(workspace, status);
        await Seed(run);

        return (project, workspace, run);
    }

    /// <summary>A run whose plan completed and that waits to be applied or rejected.</summary>
    private async Task<(Project Project, Workspace Workspace, Run Run)> SeedPlannedRun()
    {
        var (project, workspace, run) = await SeedRun(RunStatus.Planned);
        await Seed(TestData.Plan(run));

        return (project, workspace, run);
    }

    private async Task<Run> StoredRun(Guid id)
    {
        await using var context = NewContext();

        return await context.Runs.SingleOrDefaultAsync(x => x.Id == id, Ct);
    }

    private async Task<bool> AnyRun(Guid workspaceId)
    {
        await using var context = NewContext();

        return await context.Runs.AnyAsync(x => x.WorkspaceId == workspaceId, Ct);
    }
}
