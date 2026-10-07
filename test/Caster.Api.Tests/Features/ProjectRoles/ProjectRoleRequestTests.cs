// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;
using ProjectRoleView = Caster.Api.Features.ProjectRoles.ProjectRole;

namespace Caster.Api.Tests.Features.ProjectRoles;

/// <summary>
/// <c>api/project-roles</c>: one role needs ViewRoles; the list also opens to ViewProjects and to a member
/// holding ManageProject on some project, who needs it to choose a member's role.
/// </summary>
public class ProjectRoleRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    [Fact]
    public async Task Get_returns_the_role_with_its_permissions_to_a_caller_holding_ViewRoles()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewRoles).SeedAsync();

        var response = await Client(actor).GetAsync($"api/project-roles/{TestData.ProjectRoles.Observer}", Ct);

        Assert.Equal([ProjectPermission.ViewProject], (await ReadAsync<ProjectRoleView>(response)).Permissions);
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_only_ViewProjects()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var response = await Client(actor).GetAsync($"api/project-roles/{TestData.ProjectRoles.Observer}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task GetAll_lists_the_seeded_roles_for_a_member_holding_ManageProject()
    {
        var actor = await Actor().OnNewProject(ProjectPermission.ManageProject).SeedAsync();

        var response = await Client(actor).GetAsync("api/project-roles", Ct);

        var ids = (await ReadAsync<ProjectRoleView[]>(response)).Select(x => x.Id).ToArray();
        Assert.Contains(TestData.ProjectRoles.Manager, ids);
        Assert.Contains(TestData.ProjectRoles.Member, ids);
        Assert.Contains(TestData.ProjectRoles.Observer, ids);
    }

    [Fact]
    public async Task GetAll_lists_the_roles_for_a_caller_holding_ViewRoles()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewRoles).SeedAsync();

        var response = await Client(actor).GetAsync("api/project-roles", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
    }

    [Fact]
    public async Task GetAll_is_forbidden_for_a_caller_holding_only_EditProject_on_a_project()
    {
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).GetAsync("api/project-roles", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }
}
