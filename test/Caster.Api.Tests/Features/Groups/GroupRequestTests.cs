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
using GroupMembershipView = Caster.Api.Features.Groups.GroupMembership;
using GroupView = Caster.Api.Features.Groups.Group;

namespace Caster.Api.Tests.Features.Groups;

/// <summary>
/// <c>api/groups</c>: groups are created and deleted with ManageGroups, read with ViewGroups or by the
/// group's own managers, and their memberships managed with ManageGroups or by the group's managers.
/// </summary>
public class GroupRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    // ---- GET api/groups/{id} --------------------------------------------------------------------

    [Fact]
    public async Task Get_returns_the_group_to_a_caller_holding_ViewGroups()
    {
        var group = await SeedGroup();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewGroups).SeedAsync();

        var response = await Client(actor).GetAsync($"api/groups/{group.Id}", Ct);

        Assert.Equal(group.Name, (await ReadAsync<GroupView>(response)).Name);
    }

    [Fact]
    public async Task Get_returns_the_group_to_its_manager()
    {
        var group = await SeedGroup();
        var actor = await Actor().InGroup(group, GroupMembershipRole.Manager).SeedAsync();

        var response = await Client(actor).GetAsync($"api/groups/{group.Id}", Ct);

        Assert.Equal(group.Id, (await ReadAsync<GroupView>(response)).Id);
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_plain_member_of_the_group()
    {
        var group = await SeedGroup();
        var actor = await Actor().InGroup(group).SeedAsync();

        var response = await Client(actor).GetAsync($"api/groups/{group.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_managing_only_another_group()
    {
        var group = await SeedGroup();
        var actor = await Actor().OnNewGroup(GroupMembershipRole.Manager).SeedAsync();

        var response = await Client(actor).GetAsync($"api/groups/{group.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_only_ManageGroups()
    {
        var group = await SeedGroup();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageGroups).SeedAsync();

        var response = await Client(actor).GetAsync($"api/groups/{group.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    // ---- GET api/groups -------------------------------------------------------------------------

    [Fact]
    public async Task GetAll_lists_every_group_for_a_caller_holding_ViewGroups()
    {
        var group = await SeedGroup();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewGroups).SeedAsync();

        var response = await Client(actor).GetAsync("api/groups", Ct);

        Assert.Contains(group.Id, (await ReadAsync<GroupView[]>(response)).Select(x => x.Id));
    }

    [Fact]
    public async Task GetAll_lists_every_group_for_a_member_holding_ManageProject()
    {
        var group = await SeedGroup();
        var actor = await Actor().OnNewProject(ProjectPermission.ManageProject).SeedAsync();

        var response = await Client(actor).GetAsync("api/groups", Ct);

        Assert.Contains(group.Id, (await ReadAsync<GroupView[]>(response)).Select(x => x.Id));
    }

    [Fact]
    public async Task GetAll_lists_only_the_managed_group_for_a_group_manager()
    {
        await SeedGroup();
        var actor = await Actor().OnNewGroup(GroupMembershipRole.Manager).SeedAsync();

        var response = await Client(actor).GetAsync("api/groups", Ct);

        Assert.Equal([actor.GroupMembership.GroupId], (await ReadAsync<GroupView[]>(response)).Select(x => x.Id));
    }

    [Fact]
    public async Task GetAll_is_forbidden_for_a_plain_member_of_a_group()
    {
        var actor = await Actor().OnNewGroup(GroupMembershipRole.Member).SeedAsync();

        var response = await Client(actor).GetAsync("api/groups", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task GetAll_is_forbidden_for_a_caller_holding_only_EditProject_on_a_project()
    {
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).GetAsync("api/groups", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    // ---- POST api/groups, PUT api/groups, DELETE api/groups/{id} --------------------------------

    [Fact]
    public async Task Create_by_a_caller_holding_ManageGroups_stores_the_group()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageGroups).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/groups", new { name = "White Cell" }, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        Assert.NotNull(await StoredGroup("White Cell"));
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_caller_holding_only_ViewGroups()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewGroups).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/groups", new { name = "Refused" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Null(await StoredGroup("Refused"));
    }

    /// <summary>A name another group holds is answered with a 500 on create.</summary>
    [Fact]
    public async Task Create_answers_a_name_that_is_already_taken_with_a_server_error()
    {
        await SeedGroup("Taken");

        var response = await RootClient.PostAsJsonAsync("api/groups", new { name = "Taken" }, Ct);

        var problem = await AssertProblem(HttpStatusCode.InternalServerError, response);

        Assert.Equal("An error occurred while saving the entity changes. See the inner exception for details.", problem.Detail);
    }

    [Fact]
    public async Task Edit_by_a_caller_holding_ManageGroups_stores_the_new_name()
    {
        var group = await SeedGroup("Before");
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageGroups).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync("api/groups", new { id = group.Id, name = "After" }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.NotNull(await StoredGroup("After"));
    }

    /// <summary>A group's manager is refused renaming it.</summary>
    [Fact]
    public async Task Edit_is_forbidden_for_the_groups_manager()
    {
        var group = await SeedGroup("Before");
        var actor = await Actor().InGroup(group, GroupMembershipRole.Manager).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync("api/groups", new { id = group.Id, name = "After" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.NotNull(await StoredGroup("Before"));
    }

    [Fact]
    public async Task Edit_is_forbidden_for_a_caller_holding_only_ViewGroups()
    {
        var group = await SeedGroup("Before");
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewGroups).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync("api/groups", new { id = group.Id, name = "After" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.NotNull(await StoredGroup("Before"));
    }

    /// <summary>A name another group holds is answered with a 500 on edit.</summary>
    [Fact]
    public async Task Edit_answers_a_name_that_is_already_taken_with_a_server_error()
    {
        await SeedGroup("Taken");
        var group = await SeedGroup("Mine");

        var response = await RootClient.PutAsJsonAsync("api/groups", new { id = group.Id, name = "Taken" }, Ct);

        var problem = await AssertProblem(HttpStatusCode.InternalServerError, response);

        Assert.Equal("An error occurred while saving the entity changes. See the inner exception for details.", problem.Detail);
    }

    [Fact]
    public async Task Delete_by_a_caller_holding_ManageGroups_removes_the_group()
    {
        var group = await SeedGroup("Gone");
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageGroups).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/groups/{group.Id}", Ct);

        await AssertStatus(HttpStatusCode.NoContent, response);
        Assert.Null(await StoredGroup("Gone"));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_the_groups_manager()
    {
        var group = await SeedGroup("Kept");
        var actor = await Actor().InGroup(group, GroupMembershipRole.Manager).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/groups/{group.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.NotNull(await StoredGroup("Kept"));
    }

    // ---- memberships ----------------------------------------------------------------------------

    [Fact]
    public async Task GetMemberships_lists_the_members_for_the_groups_manager()
    {
        var group = await SeedGroup();
        var actor = await Actor().InGroup(group, GroupMembershipRole.Manager).SeedAsync();

        var response = await Client(actor).GetAsync($"api/groups/{group.Id}/memberships", Ct);

        Assert.Equal([actor.Id], (await ReadAsync<GroupMembershipView[]>(response)).Select(x => x.UserId));
    }

    [Fact]
    public async Task GetMemberships_lists_the_members_for_a_caller_holding_ViewGroups()
    {
        var group = await SeedGroup();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewGroups).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).GetAsync($"api/groups/{group.Id}/memberships", Ct));
    }

    [Fact]
    public async Task GetMemberships_is_forbidden_for_a_caller_managing_only_another_group()
    {
        var group = await SeedGroup();
        var actor = await Actor().OnNewGroup(GroupMembershipRole.Manager).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/groups/{group.Id}/memberships", Ct));
    }

    [Fact]
    public async Task GetMembership_returns_the_membership_to_the_groups_manager()
    {
        var (group, membership) = await SeedGroupWithAMember();
        var actor = await Actor().InGroup(group, GroupMembershipRole.Manager).SeedAsync();

        var response = await Client(actor).GetAsync($"api/groups/membership/{membership.Id}", Ct);

        Assert.Equal(membership.UserId, (await ReadAsync<GroupMembershipView>(response)).UserId);
    }

    [Fact]
    public async Task GetMembership_is_forbidden_for_a_plain_member_of_the_group()
    {
        var (group, membership) = await SeedGroupWithAMember();
        var actor = await Actor().InGroup(group).SeedAsync();

        var response = await Client(actor).GetAsync($"api/groups/membership/{membership.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task GetMembership_is_forbidden_for_a_caller_managing_only_another_group()
    {
        var (_, membership) = await SeedGroupWithAMember();
        var actor = await Actor().OnNewGroup(GroupMembershipRole.Manager).SeedAsync();

        var response = await Client(actor).GetAsync($"api/groups/membership/{membership.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task CreateMembership_by_the_groups_manager_adds_the_user()
    {
        var group = await SeedGroup();
        var user = await SeedUser();
        var actor = await Actor().InGroup(group, GroupMembershipRole.Manager).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/groups/{group.Id}/memberships", new { userId = user.Id }, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        Assert.Equal(GroupMembershipRole.Member, (await StoredMembership(group.Id, user.Id)).Role);
    }

    [Fact]
    public async Task CreateMembership_by_a_caller_holding_ManageGroups_adds_the_user()
    {
        var group = await SeedGroup();
        var user = await SeedUser();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageGroups).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/groups/{group.Id}/memberships", new { userId = user.Id }, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        Assert.NotNull(await StoredMembership(group.Id, user.Id));
    }

    [Fact]
    public async Task CreateMembership_is_forbidden_for_a_plain_member_of_the_group()
    {
        var group = await SeedGroup();
        var user = await SeedUser();
        var actor = await Actor().InGroup(group).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/groups/{group.Id}/memberships", new { userId = user.Id }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Null(await StoredMembership(group.Id, user.Id));
    }

    [Fact]
    public async Task CreateMembership_is_forbidden_for_a_caller_managing_only_another_group()
    {
        var group = await SeedGroup();
        var user = await SeedUser();
        var actor = await Actor().OnNewGroup(GroupMembershipRole.Manager).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/groups/{group.Id}/memberships", new { userId = user.Id }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Null(await StoredMembership(group.Id, user.Id));
    }

    [Fact]
    public async Task CreateMembership_is_forbidden_for_a_caller_holding_only_ViewGroups()
    {
        var group = await SeedGroup();
        var user = await SeedUser();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewGroups).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/groups/{group.Id}/memberships", new { userId = user.Id }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task CreateMembership_of_a_user_already_in_the_group_is_a_conflict()
    {
        var (group, membership) = await SeedGroupWithAMember();

        var response = await RootClient.PostAsJsonAsync($"api/groups/{group.Id}/memberships", new { userId = membership.UserId }, Ct);

        await AssertProblem(HttpStatusCode.Conflict, response);
    }

    [Fact]
    public async Task CreateMembership_broadcasts_the_new_membership_to_the_group()
    {
        var group = await SeedGroup();
        var user = await SeedUser();

        await RootClient.PostAsJsonAsync($"api/groups/{group.Id}/memberships", new { userId = user.Id }, Ct);

        var broadcast = Assert.Single(Factory.Hub<ProjectHub>().ToGroup(group.Id));
        Assert.Equal(ProjectHubMethods.GroupMembershipCreated, broadcast.Method);
        Assert.Equal(user.Id, Assert.IsType<GroupMembershipView>(broadcast.Arguments[0]).UserId);
    }

    [Fact]
    public async Task EditMembership_by_the_groups_manager_stores_the_new_role()
    {
        var (group, membership) = await SeedGroupWithAMember();
        var actor = await Actor().InGroup(group, GroupMembershipRole.Manager).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/groups/memberships/{membership.Id}", new { role = "Manager" }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(GroupMembershipRole.Manager, (await StoredMembership(group.Id, membership.UserId)).Role);
    }

    [Fact]
    public async Task EditMembership_is_forbidden_for_a_plain_member_of_the_group()
    {
        var (group, membership) = await SeedGroupWithAMember();
        var actor = await Actor().InGroup(group).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/groups/memberships/{membership.Id}", new { role = "Manager" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(GroupMembershipRole.Member, (await StoredMembership(group.Id, membership.UserId)).Role);
    }

    [Fact]
    public async Task EditMembership_is_forbidden_for_a_caller_managing_only_another_group()
    {
        var (group, membership) = await SeedGroupWithAMember();
        var actor = await Actor().OnNewGroup(GroupMembershipRole.Manager).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/groups/memberships/{membership.Id}", new { role = "Manager" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(GroupMembershipRole.Member, (await StoredMembership(group.Id, membership.UserId)).Role);
    }

    /// <summary>A role number outside <see cref="GroupMembershipRole"/> is stored as sent.</summary>
    [Fact]
    public async Task EditMembership_stores_a_role_outside_the_enum()
    {
        var (group, membership) = await SeedGroupWithAMember();

        var response = await RootClient.PutAsJsonAsync($"api/groups/memberships/{membership.Id}", new { role = 7 }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal((GroupMembershipRole)7, (await StoredMembership(group.Id, membership.UserId)).Role);
    }

    [Fact]
    public async Task DeleteMembership_by_the_groups_manager_removes_the_membership()
    {
        var (group, membership) = await SeedGroupWithAMember();
        var actor = await Actor().InGroup(group, GroupMembershipRole.Manager).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/groups/memberships/{membership.Id}", Ct);

        await AssertStatus(HttpStatusCode.NoContent, response);
        Assert.Null(await StoredMembership(group.Id, membership.UserId));
    }

    [Fact]
    public async Task DeleteMembership_is_forbidden_for_a_caller_managing_only_another_group()
    {
        var (group, membership) = await SeedGroupWithAMember();
        var actor = await Actor().OnNewGroup(GroupMembershipRole.Manager).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/groups/memberships/{membership.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.NotNull(await StoredMembership(group.Id, membership.UserId));
    }

    [Fact]
    public async Task DeleteMembership_is_forbidden_for_a_plain_member_of_the_group()
    {
        var (group, membership) = await SeedGroupWithAMember();
        var actor = await Actor().InGroup(group).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/groups/memberships/{membership.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.NotNull(await StoredMembership(group.Id, membership.UserId));
    }

    [Fact]
    public async Task DeleteMembership_broadcasts_the_removed_id_to_the_group()
    {
        var (group, membership) = await SeedGroupWithAMember();

        await RootClient.DeleteAsync($"api/groups/memberships/{membership.Id}", Ct);

        var broadcast = Assert.Single(Factory.Hub<ProjectHub>().ToGroup(group.Id));
        Assert.Equal(ProjectHubMethods.GroupMembershipDeleted, broadcast.Method);
        Assert.Equal(membership.Id, broadcast.Argument);
    }

    // ---- helpers --------------------------------------------------------------------------------

    private async Task<Group> SeedGroup(string name = null)
    {
        var group = TestData.Group(name);
        await Seed(group);

        return group;
    }

    private async Task<User> SeedUser()
    {
        var user = TestData.User(name: "Joining");
        await Seed(user);

        return user;
    }

    /// <summary>A group with one other user in it as a plain Member.</summary>
    private async Task<(Group Group, GroupMembership Membership)> SeedGroupWithAMember()
    {
        var group = TestData.Group();
        var user = TestData.User(name: "Member");
        var membership = TestData.GroupMembership(group.Id, user.Id);
        await Seed(group, user, membership);

        return (group, membership);
    }

    private async Task<Group> StoredGroup(string name)
    {
        await using var context = NewContext();

        return await context.Groups.SingleOrDefaultAsync(x => x.Name == name, Ct);
    }

    private async Task<GroupMembership> StoredMembership(Guid groupId, Guid userId)
    {
        await using var context = NewContext();

        return await context.GroupMemberships.SingleOrDefaultAsync(x => x.GroupId == groupId && x.UserId == userId, Ct);
    }
}
