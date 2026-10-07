// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;

namespace Caster.Api.Tests.Infrastructure.Authorization;

/// <summary>
/// <c>AuthorizationService</c>: the system permissions grant first; otherwise the resource is resolved to its
/// project (or group) through the database and the membership claim decides.
/// </summary>
public class AuthorizationServiceTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task A_run_is_resolved_to_its_workspaces_project()
    {
        var (project, run, _) = await SeedPlannedRun();
        var service = AuthorizationHarness.CreateCasterAuthorizationService(
            Db, new ClaimsPrincipalBuilder().WithProject(project.Id, ProjectPermission.ViewProject).Build());

        Assert.True(await service.Authorize<Run>(run.Id, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], Ct));
    }

    [Fact]
    public async Task A_plan_is_resolved_to_its_runs_project()
    {
        var (project, _, plan) = await SeedPlannedRun();
        var service = AuthorizationHarness.CreateCasterAuthorizationService(
            Db, new ClaimsPrincipalBuilder().WithProject(project.Id, ProjectPermission.ViewProject).Build());

        Assert.True(await service.Authorize<Plan>(plan.Id, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], Ct));
    }

    [Fact]
    public async Task A_resource_that_does_not_exist_resolves_to_no_project_and_is_refused()
    {
        var service = AuthorizationHarness.CreateCasterAuthorizationService(
            Db, new ClaimsPrincipalBuilder().WithProject(Guid.NewGuid(), ProjectPermission.ViewProject).Build());

        Assert.False(await service.Authorize<Workspace>(Guid.NewGuid(), [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], Ct));
    }

    [Fact]
    public async Task A_system_permission_grants_without_resolving_the_resource()
    {
        var service = AuthorizationHarness.CreateCasterAuthorizationService(
            Db, new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.EditProjects).Build());

        Assert.True(await service.Authorize<Host>(Guid.NewGuid(), [SystemPermission.EditProjects], [ProjectPermission.EditProject], Ct));
    }

    /// <summary>A resource type with no project lookup throws once the system permission does not grant.</summary>
    [Fact]
    public async Task A_resource_type_without_a_project_lookup_throws_for_a_member()
    {
        var service = AuthorizationHarness.CreateCasterAuthorizationService(
            Db, new ClaimsPrincipalBuilder().WithProject(Guid.NewGuid(), ProjectPermission.EditProject).Build());

        await Assert.ThrowsAsync<NotImplementedException>(
            () => service.Authorize<Host>(Guid.NewGuid(), [SystemPermission.EditProjects], [ProjectPermission.EditProject], Ct));
    }

    [Fact]
    public void GetAuthorizedProjectIds_lists_every_project_a_claim_names()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var service = AuthorizationHarness.CreateCasterAuthorizationService(
            Db, new ClaimsPrincipalBuilder().WithProject(first).WithProject(second, ProjectPermission.ViewProject).Build());

        Assert.Equal(new[] { first, second }.Order(), service.GetAuthorizedProjectIds().Order());
    }

    private async Task<(Project Project, Run Run, Plan Plan)> SeedPlannedRun()
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
