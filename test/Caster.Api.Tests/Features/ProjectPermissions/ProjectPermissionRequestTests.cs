// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Tests.Support;

namespace Caster.Api.Tests.Features.ProjectPermissions;

/// <summary><c>GET api/permissions/project/mine</c>: the caller's project permission claims, optionally for one project.</summary>
public class ProjectPermissionRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    [Fact]
    public async Task GetMine_returns_one_claim_per_project_the_caller_is_on()
    {
        var actor = await Actor()
            .OnNewProject(ProjectPermission.ViewProject)
            .OnNewProject(ProjectPermission.EditProject)
            .SeedAsync();

        var response = await Client(actor).GetAsync("api/permissions/project/mine", Ct);

        var claims = await ReadAsync<ProjectPermissionsClaim[]>(response);
        Assert.Equal(actor.ProjectMemberships.Select(x => x.ProjectId).Order(), claims.Select(x => x.ProjectId).Order());
    }

    [Fact]
    public async Task GetMine_for_one_project_returns_only_its_claim()
    {
        var actor = await Actor()
            .OnNewProject(ProjectPermission.ViewProject)
            .OnNewProject(ProjectPermission.EditProject)
            .SeedAsync();
        var second = actor.ProjectMemberships[1].ProjectId;

        var response = await Client(actor).GetAsync($"api/permissions/project/mine?projectId={second}", Ct);

        var claim = Assert.Single(await ReadAsync<ProjectPermissionsClaim[]>(response));
        Assert.Equal([ProjectPermission.EditProject], claim.Permissions);
    }

    [Fact]
    public async Task GetMine_returns_nothing_to_a_caller_holding_only_system_permissions()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageProjects).SeedAsync();

        var response = await Client(actor).GetAsync("api/permissions/project/mine", Ct);

        Assert.Empty(await ReadAsync<ProjectPermissionsClaim[]>(response));
    }
}
