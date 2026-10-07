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
using UserView = Caster.Api.Features.Users.User;

namespace Caster.Api.Tests.Features.Users;

/// <summary>
/// <c>api/users</c>: one user needs ViewUsers; the list also opens to ViewProjects, a project manager and a
/// group manager; every write needs ManageUsers.
/// </summary>
public class UserRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    [Fact]
    public async Task Get_returns_the_user_to_a_caller_holding_ViewUsers()
    {
        var user = await SeedUser();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewUsers).SeedAsync();

        var response = await Client(actor).GetAsync($"api/users/{user.Id}", Ct);

        Assert.Equal("Someone", (await ReadAsync<UserView>(response)).Name);
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_only_ViewProjects()
    {
        var user = await SeedUser();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var response = await Client(actor).GetAsync($"api/users/{user.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task GetAll_lists_the_users_for_a_caller_holding_ViewUsers()
    {
        var user = await SeedUser();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewUsers).SeedAsync();

        var response = await Client(actor).GetAsync("api/users", Ct);

        Assert.Contains(user.Id, (await ReadAsync<UserView[]>(response)).Select(x => x.Id));
    }

    [Fact]
    public async Task GetAll_lists_the_users_for_a_caller_holding_ViewProjects()
    {
        var user = await SeedUser();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var response = await Client(actor).GetAsync("api/users", Ct);

        Assert.Contains(user.Id, (await ReadAsync<UserView[]>(response)).Select(x => x.Id));
    }

    [Fact]
    public async Task GetAll_lists_the_users_for_a_member_holding_ManageProject()
    {
        var actor = await Actor().OnNewProject(ProjectPermission.ManageProject).SeedAsync();

        var response = await Client(actor).GetAsync("api/users", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
    }

    [Fact]
    public async Task GetAll_lists_the_users_for_a_group_manager()
    {
        var actor = await Actor().OnNewGroup(GroupMembershipRole.Manager).SeedAsync();

        var response = await Client(actor).GetAsync("api/users", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
    }

    [Fact]
    public async Task GetAll_is_forbidden_for_a_caller_holding_only_EditProject_on_a_project()
    {
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).GetAsync("api/users", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task GetAll_is_forbidden_for_a_plain_member_of_a_group()
    {
        var actor = await Actor().OnNewGroup(GroupMembershipRole.Member).SeedAsync();

        var response = await Client(actor).GetAsync("api/users", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Create_by_a_caller_holding_ManageUsers_stores_the_user_with_the_role()
    {
        var id = Guid.NewGuid();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageUsers).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(
            "api/users", new { id, name = "Created", roleId = TestData.Roles.Observer.ToString() }, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        var stored = await StoredUser(id);
        Assert.Equal("Created", stored.Name);
        Assert.Equal(TestData.Roles.Observer, stored.RoleId);
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_caller_holding_only_ViewUsers()
    {
        var id = Guid.NewGuid();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewUsers).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/users", new { id, name = "Refused" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Null(await StoredUser(id));
    }

    /// <summary>An id another user already has is answered with a 500 on create.</summary>
    [Fact]
    public async Task Create_answers_an_id_that_is_already_taken_with_a_server_error()
    {
        var user = await SeedUser();

        var response = await RootClient.PostAsJsonAsync("api/users", new { id = user.Id, name = "Again" }, Ct);

        await AssertProblem(HttpStatusCode.InternalServerError, response);
    }

    [Fact]
    public async Task Edit_by_a_caller_holding_ManageUsers_stores_the_new_role()
    {
        var user = await SeedUser();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageUsers).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync(
            $"api/users/{user.Id}", new { name = "Someone", roleId = TestData.Roles.ContentDeveloper.ToString() }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(TestData.Roles.ContentDeveloper, (await StoredUser(user.Id)).RoleId);
    }

    [Fact]
    public async Task Edit_is_forbidden_for_a_caller_holding_only_ViewUsers()
    {
        var user = await SeedUser();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewUsers).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync(
            $"api/users/{user.Id}", new { name = "Someone", roleId = TestData.Roles.Administrator.ToString() }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Null((await StoredUser(user.Id)).RoleId);
    }

    [Fact]
    public async Task Delete_by_a_caller_holding_ManageUsers_removes_the_user()
    {
        var user = await SeedUser();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageUsers).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/users/{user.Id}", Ct);

        await AssertStatus(HttpStatusCode.NoContent, response);
        Assert.Null(await StoredUser(user.Id));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_holding_only_ViewUsers()
    {
        var user = await SeedUser();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewUsers).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/users/{user.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.NotNull(await StoredUser(user.Id));
    }

    /// <summary>The first request of an unknown user creates their row from the token's name.</summary>
    [Fact]
    public async Task A_first_request_from_an_unknown_user_creates_their_user_row()
    {
        var id = Guid.NewGuid();

        await ClientFor(id, "Newcomer").GetAsync("api/permissions/mine", Ct);

        Assert.Equal("Newcomer", (await StoredUser(id)).Name);
    }

    private async Task<User> SeedUser()
    {
        var user = TestData.User(name: "Someone");
        await Seed(user);

        return user;
    }

    private async Task<User> StoredUser(Guid id)
    {
        await using var context = NewContext();

        return await context.Users.SingleOrDefaultAsync(x => x.Id == id, Ct);
    }
}
