// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Hubs;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using MembershipView = Caster.Api.Features.Projects.ProjectMembership;

namespace Caster.Api.Tests.Features.Projects;

/// <summary>
/// The membership endpoints of <c>ProjectsController</c>: who may list, read, add, change and remove a
/// project's members, what each write stores, and the broadcast to the project's admin group.
/// </summary>
public class ProjectMembershipRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    // ---- GET api/projects/{projectId}/memberships -----------------------------------------------

    [Fact]
    public async Task GetMemberships_lists_the_members_for_a_member_holding_ViewProject()
    {
        var project = await SeedProject();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/projects/{project.Id}/memberships", Ct);

        Assert.Equal([actor.ProjectMembership.MembershipId], (await ReadAsync<MembershipView[]>(response)).Select(x => x.Id));
    }

    [Fact]
    public async Task GetMemberships_lists_the_members_for_a_caller_holding_ViewProjects()
    {
        var project = await SeedProject();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var response = await Client(actor).GetAsync($"api/projects/{project.Id}/memberships", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
    }

    [Fact]
    public async Task GetMemberships_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var project = await SeedProject();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/projects/{project.Id}/memberships", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task GetMemberships_is_forbidden_for_a_caller_holding_ViewProject_only_on_another_project()
    {
        var project = await SeedProject();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        var response = await Client(actor).GetAsync($"api/projects/{project.Id}/memberships", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    // ---- GET api/projects/memberships/{id} ------------------------------------------------------

    [Fact]
    public async Task GetMembership_returns_the_membership_to_a_member_holding_ManageProject()
    {
        var project = await SeedProject();
        var actor = await Actor().OnProject(project, [ProjectPermission.ManageProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/projects/memberships/{actor.ProjectMembership.MembershipId}", Ct);

        Assert.Equal(project.Id, (await ReadAsync<MembershipView>(response)).ProjectId);
    }

    [Fact]
    public async Task GetMembership_returns_the_membership_to_a_caller_holding_ManageProjects()
    {
        var (_, membership) = await SeedProjectWithAMember();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageProjects).SeedAsync();

        var response = await Client(actor).GetAsync($"api/projects/memberships/{membership.Id}", Ct);

        Assert.Equal(membership.Id, (await ReadAsync<MembershipView>(response)).Id);
    }

    /// <summary>A member who may list every membership of a project may not read one of them.</summary>
    [Fact]
    public async Task GetMembership_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, membership) = await SeedProjectWithAMember();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/projects/memberships/{membership.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task GetMembership_is_forbidden_for_a_caller_holding_ManageProject_only_on_another_project()
    {
        var (_, membership) = await SeedProjectWithAMember();
        var actor = await Actor().OnNewProject(ProjectPermission.ManageProject).SeedAsync();

        var response = await Client(actor).GetAsync($"api/projects/memberships/{membership.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task GetMembership_is_forbidden_for_a_caller_holding_only_ViewProjects()
    {
        var (_, membership) = await SeedProjectWithAMember();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var response = await Client(actor).GetAsync($"api/projects/memberships/{membership.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    // ---- POST api/projects/{projectId}/memberships ----------------------------------------------

    [Fact]
    public async Task CreateMembership_by_a_member_holding_ManageProject_adds_the_user_with_the_member_role()
    {
        var project = await SeedProject();
        var user = await SeedUser();
        var actor = await Actor().OnProject(project, [ProjectPermission.ManageProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/projects/{project.Id}/memberships", new { userId = user.Id }, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        var stored = await StoredMembership(project.Id, user.Id);
        Assert.Equal(TestData.ProjectRoles.Member, stored.RoleId);
    }

    [Fact]
    public async Task CreateMembership_by_a_caller_holding_ManageProjects_adds_a_group()
    {
        var project = await SeedProject();
        var group = TestData.Group();
        await Seed(group);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageProjects).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/projects/{project.Id}/memberships", new { groupId = group.Id }, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        await using var context = NewContext();
        Assert.True(await context.ProjectMemberships.AnyAsync(x => x.ProjectId == project.Id && x.GroupId == group.Id, Ct));
    }

    [Fact]
    public async Task CreateMembership_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var project = await SeedProject();
        var user = await SeedUser();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/projects/{project.Id}/memberships", new { userId = user.Id }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Null(await StoredMembership(project.Id, user.Id));
    }

    [Fact]
    public async Task CreateMembership_is_forbidden_for_a_caller_holding_ManageProject_only_on_another_project()
    {
        var project = await SeedProject();
        var user = await SeedUser();
        var actor = await Actor().OnNewProject(ProjectPermission.ManageProject).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/projects/{project.Id}/memberships", new { userId = user.Id }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Null(await StoredMembership(project.Id, user.Id));
    }

    [Fact]
    public async Task CreateMembership_is_forbidden_for_a_caller_holding_only_EditProjects()
    {
        var project = await SeedProject();
        var user = await SeedUser();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/projects/{project.Id}/memberships", new { userId = user.Id }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task CreateMembership_of_a_user_already_on_the_project_is_a_conflict()
    {
        var (project, membership) = await SeedProjectWithAMember();

        var response = await RootClient.PostAsJsonAsync($"api/projects/{project.Id}/memberships", new { userId = membership.UserId }, Ct);

        await AssertProblem(HttpStatusCode.Conflict, response);
    }

    /// <summary>A membership naming neither a user nor a group is stored.</summary>
    [Fact]
    public async Task CreateMembership_with_neither_a_user_nor_a_group_stores_an_empty_membership()
    {
        var project = await SeedProject();

        var response = await RootClient.PostAsJsonAsync($"api/projects/{project.Id}/memberships", new { }, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        await using var context = NewContext();
        Assert.True(await context.ProjectMemberships.AnyAsync(x => x.ProjectId == project.Id && x.UserId == null && x.GroupId == null, Ct));
    }

    [Fact]
    public async Task CreateMembership_broadcasts_the_new_membership_to_the_projects_admin_group()
    {
        var project = await SeedProject();
        var user = await SeedUser();

        await RootClient.PostAsJsonAsync($"api/projects/{project.Id}/memberships", new { userId = user.Id }, Ct);

        var broadcast = Assert.Single(Factory.Hub<ProjectHub>().ToGroup(ProjectHubMethods.GetProjectAdminGroup(project.Id)));
        Assert.Equal(ProjectHubMethods.ProjectMembershipCreated, broadcast.Method);
        Assert.Equal(user.Id, Assert.IsType<MembershipView>(broadcast.Arguments[0]).UserId);
    }

    // ---- PUT api/projects/memberships -----------------------------------------------------------

    [Fact]
    public async Task EditMembership_by_a_member_holding_ManageProject_stores_the_new_role()
    {
        var (project, membership) = await SeedProjectWithAMember();
        var actor = await Actor().OnProject(project, [ProjectPermission.ManageProject]).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync("api/projects/memberships", new { id = membership.Id, roleId = TestData.ProjectRoles.Manager }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(TestData.ProjectRoles.Manager, (await StoredMembership(project.Id, membership.UserId.Value)).RoleId);
    }

    [Fact]
    public async Task EditMembership_by_a_caller_holding_ManageProjects_stores_the_new_role()
    {
        var (project, membership) = await SeedProjectWithAMember();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageProjects).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync("api/projects/memberships", new { id = membership.Id, roleId = TestData.ProjectRoles.Manager }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(TestData.ProjectRoles.Manager, (await StoredMembership(project.Id, membership.UserId.Value)).RoleId);
    }

    [Fact]
    public async Task EditMembership_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var (project, membership) = await SeedProjectWithAMember();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync("api/projects/memberships", new { id = membership.Id, roleId = TestData.ProjectRoles.Manager }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(TestData.ProjectRoles.Observer, (await StoredMembership(project.Id, membership.UserId.Value)).RoleId);
    }

    [Fact]
    public async Task EditMembership_is_forbidden_for_a_caller_holding_ManageProject_only_on_another_project()
    {
        var (project, membership) = await SeedProjectWithAMember();
        var actor = await Actor().OnNewProject(ProjectPermission.ManageProject).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync("api/projects/memberships", new { id = membership.Id, roleId = TestData.ProjectRoles.Manager }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(TestData.ProjectRoles.Observer, (await StoredMembership(project.Id, membership.UserId.Value)).RoleId);
    }

    [Fact]
    public async Task EditMembership_to_an_unknown_role_is_a_bad_request()
    {
        var (_, membership) = await SeedProjectWithAMember();

        var response = await RootClient.PutAsJsonAsync("api/projects/memberships", new { id = membership.Id, roleId = Guid.NewGuid() }, Ct);

        await AssertProblem(HttpStatusCode.BadRequest, response);
    }

    // ---- DELETE api/projects/memberships/{id} ---------------------------------------------------

    [Fact]
    public async Task DeleteMembership_by_a_member_holding_ManageProject_removes_the_membership()
    {
        var (project, membership) = await SeedProjectWithAMember();
        var actor = await Actor().OnProject(project, [ProjectPermission.ManageProject]).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/projects/memberships/{membership.Id}", Ct);

        await AssertStatus(HttpStatusCode.NoContent, response);
        Assert.Null(await StoredMembership(project.Id, membership.UserId.Value));
    }

    [Fact]
    public async Task DeleteMembership_by_a_caller_holding_ManageProjects_removes_the_membership()
    {
        var (project, membership) = await SeedProjectWithAMember();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageProjects).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/projects/memberships/{membership.Id}", Ct);

        await AssertStatus(HttpStatusCode.NoContent, response);
        Assert.Null(await StoredMembership(project.Id, membership.UserId.Value));
    }

    [Fact]
    public async Task DeleteMembership_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var (project, membership) = await SeedProjectWithAMember();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/projects/memberships/{membership.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.NotNull(await StoredMembership(project.Id, membership.UserId.Value));
    }

    [Fact]
    public async Task DeleteMembership_is_forbidden_for_a_caller_holding_ManageProject_only_on_another_project()
    {
        var (project, membership) = await SeedProjectWithAMember();
        var actor = await Actor().OnNewProject(ProjectPermission.ManageProject).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/projects/memberships/{membership.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.NotNull(await StoredMembership(project.Id, membership.UserId.Value));
    }

    [Fact]
    public async Task DeleteMembership_broadcasts_the_removed_id_to_the_projects_admin_group()
    {
        var (project, membership) = await SeedProjectWithAMember();

        await RootClient.DeleteAsync($"api/projects/memberships/{membership.Id}", Ct);

        var broadcast = Assert.Single(Factory.Hub<ProjectHub>().ToGroup(ProjectHubMethods.GetProjectAdminGroup(project.Id)));
        Assert.Equal(ProjectHubMethods.ProjectMembershipDeleted, broadcast.Method);
        Assert.Equal(membership.Id, broadcast.Argument);
    }

    // ---- helpers --------------------------------------------------------------------------------

    private async Task<Project> SeedProject()
    {
        var project = TestData.Project();
        await Seed(project);

        return project;
    }

    private async Task<User> SeedUser()
    {
        var user = TestData.User(name: "Added");
        await Seed(user);

        return user;
    }

    /// <summary>A project with one other user on it as an Observer.</summary>
    private async Task<(Project Project, ProjectMembership Membership)> SeedProjectWithAMember()
    {
        var project = TestData.Project();
        var user = TestData.User(name: "Member");
        var membership = TestData.ProjectMembership(project.Id, TestData.ProjectRoles.Observer, userId: user.Id);
        await Seed(project, user, membership);

        return (project, membership);
    }

    private async Task<ProjectMembership> StoredMembership(Guid projectId, Guid userId)
    {
        await using var context = NewContext();

        return await context.ProjectMemberships.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.UserId == userId, Ct);
    }
}
