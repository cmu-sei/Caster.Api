// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using GroupView = Caster.Api.Features.ProjectGroups.ProjectGroup;
using InventoryConnectionView = Caster.Api.Features.Inventory.InventoryConnection;
using ProjectSetView = Caster.Api.Features.EnvironmentSets.ProjectEnvironmentSet;

namespace Caster.Api.Tests.Features.ProjectGroups;

/// <summary>
/// <c>api/project-groups</c>: groups are made by ManageProjectGroups holders, managed by them and by the
/// group's managers, and give their projects their memberships, Environment Sets and connections.
/// </summary>
public class ProjectGroupRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    private const string Groups = "api/project-groups";

    private static ProjectGroup Group(string name = null) => new() { Id = Guid.NewGuid(), Name = name ?? $"group-{Guid.NewGuid():N}" };

    private static ProjectGroupMembership Membership(ProjectGroup group, Guid userId, Guid roleId) => new()
    {
        Id = Guid.NewGuid(),
        ProjectGroupId = group.Id,
        UserId = userId,
        RoleId = roleId
    };

    private async Task<(ProjectGroup Group, Project Project)> SeedGroupWithProject()
    {
        var group = Group();
        var project = TestData.Project();
        project.ProjectGroupId = group.Id;
        await Seed(group, project);
        return (group, project);
    }

    // ---- creating and listing --------------------------------------------------------------------

    [Fact]
    public async Task Create_needs_ManageProjectGroups()
    {
        var holder = await Actor().WithSystemPermissions(SystemPermission.ManageProjectGroups).SeedAsync();
        var other = await Actor().WithSystemPermissions(SystemPermission.ManageProjects).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(other).PostAsJsonAsync(Groups, new { name = "Range" }, Ct));

        var response = await Client(holder).PostAsJsonAsync(Groups, new { name = $"Range-{Guid.NewGuid():N}" }, Ct);
        await AssertStatus(HttpStatusCode.OK, response);
    }

    [Fact]
    public async Task GetAll_shows_members_only_their_groups()
    {
        var (mine, _) = await SeedGroupWithProject();
        var theirs = Group();
        await Seed(theirs);
        var member = await Actor().SeedAsync();
        await Seed(Membership(mine, member.Id, TestData.ProjectRoles.Observer));

        var groups = await ReadAsync<GroupView[]>(await Client(member).GetAsync(Groups, Ct));

        Assert.Equal([mine.Id], groups.Select(x => x.Id));
        Assert.False(groups[0].CanManage);
    }

    // ---- memberships reach every project ---------------------------------------------------------

    [Fact]
    public async Task A_group_membership_gives_its_role_in_every_project_of_the_group()
    {
        var (group, project) = await SeedGroupWithProject();
        var member = await Actor().SeedAsync();
        await Seed(Membership(group, member.Id, TestData.ProjectRoles.Observer));

        var response = await Client(member).GetAsync($"api/projects/{project.Id}", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
    }

    /// <summary>One claim per project: a project's claim is found by its first match, so a second would make access depend on order.</summary>
    [Fact]
    public async Task Direct_and_group_memberships_merge_into_one_set_of_permissions()
    {
        var (group, project) = await SeedGroupWithProject();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();
        await Seed(Membership(group, actor.Id, TestData.ProjectRoles.Manager));

        var response = await Client(actor).PutAsJsonAsync($"api/projects/{project.Id}", new { name = "Renamed" }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
    }

    // ---- managing a group's memberships and projects ---------------------------------------------

    [Fact]
    public async Task A_group_manager_can_grant_memberships_but_a_member_cannot()
    {
        var (group, _) = await SeedGroupWithProject();
        var manager = await Actor().SeedAsync();
        var member = await Actor().SeedAsync();
        var newcomer = TestData.User(name: "Newcomer");
        await Seed(newcomer, Membership(group, manager.Id, TestData.ProjectRoles.Manager), Membership(group, member.Id, TestData.ProjectRoles.Member));

        await AssertProblem(HttpStatusCode.Forbidden,
            await Client(member).PostAsJsonAsync($"{Groups}/{group.Id}/memberships", new { userId = newcomer.Id }, Ct));

        // a manager can appoint another manager, as user-group managers can
        var response = await Client(manager).PostAsJsonAsync(
            $"{Groups}/{group.Id}/memberships", new { userId = newcomer.Id, roleId = TestData.ProjectRoles.Manager }, Ct);
        await AssertStatus(HttpStatusCode.OK, response);
    }

    /// <summary>Otherwise a group manager could pull in any project and gain ManageProject over it.</summary>
    [Fact]
    public async Task Adding_a_project_needs_ManageProject_on_it_as_well_as_managing_the_group()
    {
        var group = Group();
        var other = TestData.Project("Someone else's");
        await Seed(group, other);
        var manager = await Actor().SeedAsync();
        await Seed(Membership(group, manager.Id, TestData.ProjectRoles.Manager));
        var owner = await Actor().OnProject(other, [ProjectPermission.ManageProject]).SeedAsync();
        await Seed(Membership(group, owner.Id, TestData.ProjectRoles.Manager));

        await AssertProblem(HttpStatusCode.Forbidden, await Client(manager).PostAsync($"{Groups}/{group.Id}/projects/{other.Id}", null, Ct));
        await AssertStatus(HttpStatusCode.OK, await Client(owner).PostAsync($"{Groups}/{group.Id}/projects/{other.Id}", null, Ct));
    }

    [Fact]
    public async Task A_projects_own_manager_cannot_take_it_out_of_its_group()
    {
        var (group, project) = await SeedGroupWithProject();
        var projectManager = await Actor().OnProject(project, [ProjectPermission.ManageProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden,
            await Client(projectManager).DeleteAsync($"{Groups}/{group.Id}/projects/{project.Id}", Ct));
        await AssertStatus(HttpStatusCode.OK, await RootClient.DeleteAsync($"{Groups}/{group.Id}/projects/{project.Id}", Ct));
    }

    [Fact]
    public async Task A_project_can_be_created_in_a_group_by_its_manager()
    {
        var group = Group();
        await Seed(group);
        var creator = await Actor().WithSystemPermissions(SystemPermission.CreateProjects).SeedAsync();
        var bystander = await Actor().WithSystemPermissions(SystemPermission.CreateProjects).SeedAsync();
        await Seed(Membership(group, creator.Id, TestData.ProjectRoles.Manager));

        await AssertProblem(HttpStatusCode.Forbidden,
            await Client(bystander).PostAsJsonAsync("api/projects", new { name = "New", projectGroupId = group.Id }, Ct));

        var response = await Client(creator).PostAsJsonAsync("api/projects", new { name = "New", projectGroupId = group.Id }, Ct);
        await AssertStatus(HttpStatusCode.Created, response);
        Assert.True(await ReadBack(db => db.Projects.AnyAsync(x => x.Name == "New" && x.ProjectGroupId == group.Id, Ct)));
    }

    // ---- what a group carries --------------------------------------------------------------------

    [Fact]
    public async Task A_groups_sets_reach_its_projects_marked_with_the_group()
    {
        var (group, project) = await SeedGroupWithProject();
        var set = TestData.EnvironmentSet("Shared");
        await Seed(set, TestData.PlainEntry(set, "REGION", "east"));
        var member = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();
        var groupManager = await Actor().SeedAsync();
        await Seed(Membership(group, groupManager.Id, TestData.ProjectRoles.Manager));

        // group managers can't give a group sets: that needs ManageEnvironmentSets
        await AssertProblem(HttpStatusCode.Forbidden,
            await Client(groupManager).PostAsync($"{Groups}/{group.Id}/environment-sets/{set.Id}", null, Ct));
        await AssertStatus(HttpStatusCode.OK, await RootClient.PostAsync($"{Groups}/{group.Id}/environment-sets/{set.Id}", null, Ct));

        var view = Assert.Single(await ReadAsync<ProjectSetView[]>(await Client(member).GetAsync($"api/projects/{project.Id}/environment-sets", Ct)));
        Assert.Equal(set.Id, view.EnvironmentSetId);
        Assert.False(view.IsDirect);
        Assert.Equal([group.Name], view.ProjectGroups.Select(x => x.Name));
    }

    [Fact]
    public async Task A_set_on_a_group_and_a_project_that_gives_the_same_name_is_refused()
    {
        var (group, project) = await SeedGroupWithProject();
        var direct = TestData.EnvironmentSet("Direct");
        var shared = TestData.EnvironmentSet("Shared");
        await Seed(direct, shared,
            TestData.PlainEntry(direct, "REGION", "east"),
            TestData.PlainEntry(shared, "REGION", "west"),
            TestData.ProjectEnvironmentSet(project, direct));

        var response = await RootClient.PostAsync($"{Groups}/{group.Id}/environment-sets/{shared.Id}", null, Ct);

        await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.Contains("REGION", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Moving_a_project_into_a_group_whose_sets_clash_with_its_own_is_refused()
    {
        var group = Group();
        var project = TestData.Project();
        var direct = TestData.EnvironmentSet("Direct");
        var shared = TestData.EnvironmentSet("Shared");
        await Seed(group, project, direct, shared,
            TestData.PlainEntry(direct, "REGION", "east"),
            TestData.PlainEntry(shared, "REGION", "west"),
            TestData.ProjectEnvironmentSet(project, direct),
            new ProjectGroupEnvironmentSet { Id = Guid.NewGuid(), ProjectGroupId = group.Id, EnvironmentSetId = shared.Id });

        await AssertProblem(HttpStatusCode.Conflict, await RootClient.PostAsync($"{Groups}/{group.Id}/projects/{project.Id}", null, Ct));
        Assert.Null(await ReadBack(db => db.Projects.Where(x => x.Id == project.Id).Select(x => x.ProjectGroupId).SingleAsync(Ct)));
    }

    [Fact]
    public async Task A_groups_connections_reach_its_projects()
    {
        var (group, project) = await SeedGroupWithProject();
        var member = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        await AssertStatus(HttpStatusCode.OK,
            await RootClient.PostAsync($"{Groups}/{group.Id}/inventory-connections/{TestConfiguration.VsphereConnection}", null, Ct));

        var connection = Assert.Single(await ReadAsync<InventoryConnectionView[]>(
            await Client(member).GetAsync($"api/projects/{project.Id}/inventory/connections", Ct)));
        Assert.Equal(TestConfiguration.VsphereConnection, connection.Name);
        Assert.False(connection.IsDirect);
        Assert.Equal([group.Name], connection.ProjectGroups);
    }

    [Fact]
    public async Task A_host_belongs_to_a_project_or_a_group_never_both()
    {
        var (group, project) = await SeedGroupWithProject();
        var host = new Host { Id = Guid.NewGuid(), Name = "esx1", MaximumMachines = 10, Enabled = true, ProjectId = project.Id };
        await Seed(host);

        // moving it to the group is one edit
        await AssertStatus(HttpStatusCode.OK, await RootClient.PutAsJsonAsync(
            $"api/hosts/{host.Id}", new { name = "esx1", maximumMachines = 10, enabled = true, projectGroupId = group.Id }, Ct));
        var stored = await ReadBack(db => db.Hosts.SingleAsync(x => x.Id == host.Id, Ct));
        Assert.Equal(group.Id, stored.ProjectGroupId);
        Assert.Null(stored.ProjectId);

        await AssertProblem(HttpStatusCode.BadRequest, await RootClient.PutAsJsonAsync(
            $"api/hosts/{host.Id}", new { name = "esx1", maximumMachines = 10, enabled = true, projectId = project.Id, projectGroupId = group.Id }, Ct));
    }

    [Fact]
    public async Task Deleting_a_group_leaves_its_projects_and_takes_its_sets_from_them()
    {
        var (group, project) = await SeedGroupWithProject();
        var set = TestData.EnvironmentSet();
        await Seed(set, new ProjectGroupEnvironmentSet { Id = Guid.NewGuid(), ProjectGroupId = group.Id, EnvironmentSetId = set.Id });

        await AssertStatus(HttpStatusCode.NoContent, await RootClient.DeleteAsync($"{Groups}/{group.Id}", Ct));

        Assert.Null(await ReadBack(db => db.Projects.Where(x => x.Id == project.Id).Select(x => x.ProjectGroupId).SingleAsync(Ct)));
        Assert.Empty(await ReadAsync<ProjectSetView[]>(await RootClient.GetAsync($"api/projects/{project.Id}/environment-sets", Ct)));
    }
}
