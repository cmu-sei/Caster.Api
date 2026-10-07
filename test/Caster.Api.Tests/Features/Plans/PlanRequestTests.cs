// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;
using PlanView = Caster.Api.Features.Plans.Plan;

namespace Caster.Api.Tests.Features.Plans;

/// <summary><c>PlansController</c>: a run's plan is read with ViewProject on its project, or ViewProjects.</summary>
public class PlanRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    [Fact]
    public async Task Get_returns_the_plan_with_its_output_to_a_member_holding_ViewProject()
    {
        var (project, _, plan) = await SeedPlan();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/plans/{plan.Id}", Ct);

        Assert.Equal(plan.Output, (await ReadAsync<PlanView>(response)).Output);
    }

    [Fact]
    public async Task Get_returns_the_plan_to_a_caller_holding_ViewProjects()
    {
        var (_, _, plan) = await SeedPlan();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).GetAsync($"api/plans/{plan.Id}", Ct));
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var (project, _, plan) = await SeedPlan();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/plans/{plan.Id}", Ct));
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_ViewProject_only_on_another_project()
    {
        var (_, _, plan) = await SeedPlan();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/plans/{plan.Id}", Ct));
    }

    [Fact]
    public async Task GetByRun_returns_the_runs_plan_to_a_member_holding_ViewProject()
    {
        var (project, run, plan) = await SeedPlan();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/runs/{run.Id}/plan", Ct);

        Assert.Equal(plan.Id, (await ReadAsync<PlanView>(response)).Id);
    }

    [Fact]
    public async Task GetByRun_returns_the_plan_to_a_caller_holding_ViewProjects()
    {
        var (_, run, _) = await SeedPlan();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).GetAsync($"api/runs/{run.Id}/plan", Ct));
    }

    [Fact]
    public async Task GetByRun_is_forbidden_for_a_caller_holding_ViewProject_only_on_another_project()
    {
        var (_, run, _) = await SeedPlan();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/runs/{run.Id}/plan", Ct));
    }

    [Fact]
    public async Task GetByRun_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var (project, run, _) = await SeedPlan();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/runs/{run.Id}/plan", Ct));
    }

    private async Task<(Project Project, Run Run, Plan Plan)> SeedPlan()
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        var workspace = TestData.Workspace(directory);
        var run = TestData.Run(workspace, RunStatus.Planned);
        var plan = TestData.Plan(run);
        await Seed(project, directory, workspace, run, plan);

        return (project, run, plan);
    }
}
