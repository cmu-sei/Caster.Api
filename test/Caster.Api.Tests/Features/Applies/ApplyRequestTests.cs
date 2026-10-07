// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using ApplyView = Caster.Api.Features.Applies.Apply;

namespace Caster.Api.Tests.Features.Applies;

/// <summary>
/// <c>AppliesController</c>: an apply is read with ViewProject (or ViewProjects) and started with EditProject
/// (or EditProjects) on the run's project. A started apply is queued; the queue is not running.
/// </summary>
public class ApplyRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    [Fact]
    public async Task Get_returns_the_apply_to_a_member_holding_ViewProject()
    {
        var (project, _, apply) = await SeedApply();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/applies/{apply.Id}", Ct);

        Assert.Equal(apply.Output, (await ReadAsync<ApplyView>(response)).Output);
    }

    [Fact]
    public async Task Get_returns_the_apply_to_a_caller_holding_ViewProjects()
    {
        var (_, _, apply) = await SeedApply();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).GetAsync($"api/applies/{apply.Id}", Ct));
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var (project, _, apply) = await SeedApply();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/applies/{apply.Id}", Ct));
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_ViewProject_only_on_another_project()
    {
        var (_, _, apply) = await SeedApply();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/applies/{apply.Id}", Ct));
    }

    [Fact]
    public async Task GetByRun_returns_the_runs_apply_to_a_member_holding_ViewProject()
    {
        var (project, run, apply) = await SeedApply();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/runs/{run.Id}/apply", Ct);

        Assert.Equal(apply.Id, (await ReadAsync<ApplyView>(response)).Id);
    }

    [Fact]
    public async Task GetByRun_returns_the_apply_to_a_caller_holding_ViewProjects()
    {
        var (_, run, _) = await SeedApply();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).GetAsync($"api/runs/{run.Id}/apply", Ct));
    }

    [Fact]
    public async Task GetByRun_is_forbidden_for_a_caller_holding_ViewProject_only_on_another_project()
    {
        var (_, run, _) = await SeedApply();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/runs/{run.Id}/apply", Ct));
    }

    [Fact]
    public async Task GetByRun_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var (project, run, _) = await SeedApply();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/runs/{run.Id}/apply", Ct));
    }

    [Fact]
    public async Task Execute_by_a_member_holding_EditProject_queues_an_apply_for_a_planned_run()
    {
        var (project, run) = await SeedPlannedRun();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsync($"api/runs/{run.Id}/actions/apply", null, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        await using var context = NewContext();
        var stored = await context.Runs.Include(x => x.Apply).SingleAsync(x => x.Id == run.Id, Ct);
        Assert.Equal(RunStatus.ApplyQueued, stored.Status);
        Assert.Equal(ApplyStatus.Queued, stored.Apply.Status);
    }

    [Fact]
    public async Task Execute_by_a_caller_holding_EditProjects_queues_an_apply()
    {
        var (_, run) = await SeedPlannedRun();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).PostAsync($"api/runs/{run.Id}/actions/apply", null, Ct));
    }

    [Fact]
    public async Task Execute_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, run) = await SeedPlannedRun();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PostAsync($"api/runs/{run.Id}/actions/apply", null, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.False(await AnyApply(run.Id));
    }

    [Fact]
    public async Task Execute_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, run) = await SeedPlannedRun();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).PostAsync($"api/runs/{run.Id}/actions/apply", null, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.False(await AnyApply(run.Id));
    }

    [Fact]
    public async Task Execute_of_a_run_that_was_already_applied_is_a_conflict()
    {
        var (_, run, _) = await SeedApply();

        await AssertProblem(HttpStatusCode.Conflict, await RootClient.PostAsync($"api/runs/{run.Id}/actions/apply", null, Ct));
    }

    private async Task<(Project Project, Run Run)> SeedPlannedRun()
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        var workspace = TestData.Workspace(directory);
        var run = TestData.Run(workspace, RunStatus.Planned);
        await Seed(project, directory, workspace, run, TestData.Plan(run));

        return (project, run);
    }

    private async Task<(Project Project, Run Run, Apply Apply)> SeedApply()
    {
        var (project, run) = await SeedPlannedRun();
        var apply = TestData.Apply(run);
        await Seed(apply);

        return (project, run, apply);
    }

    private async Task<bool> AnyApply(Guid runId)
    {
        await using var context = NewContext();

        return await context.Applies.AnyAsync(x => x.RunId == runId, Ct);
    }
}
