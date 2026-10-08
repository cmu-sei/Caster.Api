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
using SystemRoleView = Caster.Api.Features.SystemRoles.SystemRole;

namespace Caster.Api.Tests.Features.SystemRoles;

/// <summary>
/// <c>api/system-roles</c>: reading needs ViewRoles (the list also ViewProjects), every write ManageRoles,
/// and the seeded Administrator role is immutable.
/// </summary>
public class SystemRoleRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    [Fact]
    public async Task Get_returns_the_role_with_its_permissions_to_a_caller_holding_ViewRoles()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewRoles).SeedAsync();

        var response = await Client(actor).GetAsync($"api/system-roles/{TestData.Roles.ContentDeveloper}", Ct);

        Assert.Equal([SystemPermission.CreateProjects], (await ReadAsync<SystemRoleView>(response)).Permissions);
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_only_ViewProjects()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var response = await Client(actor).GetAsync($"api/system-roles/{TestData.Roles.ContentDeveloper}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task GetAll_lists_the_seeded_roles_for_a_caller_holding_ViewRoles()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewRoles).SeedAsync();

        var response = await Client(actor).GetAsync("api/system-roles", Ct);

        Assert.Contains(TestData.Roles.Administrator, (await ReadAsync<SystemRoleView[]>(response)).Select(x => x.Id));
    }

    [Fact]
    public async Task GetAll_lists_the_roles_for_a_caller_holding_ViewProjects()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var response = await Client(actor).GetAsync("api/system-roles", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
    }

    [Fact]
    public async Task GetAll_is_forbidden_for_a_caller_holding_only_ManageRoles()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageRoles).SeedAsync();

        var response = await Client(actor).GetAsync("api/system-roles", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Create_by_a_caller_holding_ManageRoles_stores_the_role_and_its_permissions()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageRoles).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(
            "api/system-roles", new { name = "Host Keeper", permissions = new[] { "ViewHosts", "ManageHosts" } }, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        var stored = await StoredRole("Host Keeper");
        Assert.Equal([SystemPermission.ViewHosts, SystemPermission.ManageHosts], stored.Permissions);
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_caller_holding_only_ViewRoles()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewRoles).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/system-roles", new { name = "Refused" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Null(await StoredRole("Refused"));
    }

    /// <summary>A name another role holds is answered with a 500 on create.</summary>
    [Fact]
    public async Task Create_answers_a_name_that_is_already_taken_with_a_server_error()
    {
        var response = await RootClient.PostAsJsonAsync("api/system-roles", new { name = "Observer" }, Ct);

        var problem = await AssertProblem(HttpStatusCode.InternalServerError, response);

        Assert.Equal("An error occurred while saving the entity changes. See the inner exception for details.", problem.Detail);
    }

    [Fact]
    public async Task Edit_by_a_caller_holding_ManageRoles_stores_the_new_permissions()
    {
        var role = TestData.SystemRole(permissions: [SystemPermission.ViewHosts]);
        await Seed(role);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageRoles).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync(
            $"api/system-roles/{role.Id}", new { name = role.Name, permissions = new[] { "ViewVLANs" } }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal([SystemPermission.ViewVLANs], (await StoredRole(role.Name)).Permissions);
    }

    [Fact]
    public async Task Edit_is_forbidden_for_a_caller_holding_only_ViewRoles()
    {
        var role = TestData.SystemRole(permissions: [SystemPermission.ViewHosts]);
        await Seed(role);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewRoles).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync(
            $"api/system-roles/{role.Id}", new { name = role.Name, permissions = new[] { "ViewVLANs" } }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal([SystemPermission.ViewHosts], (await StoredRole(role.Name)).Permissions);
    }

    [Fact]
    public async Task Edit_of_the_administrator_role_is_a_conflict()
    {
        var response = await RootClient.PutAsJsonAsync(
            $"api/system-roles/{TestData.Roles.Administrator}", new { name = "Administrator", allPermissions = false }, Ct);

        await AssertProblem(HttpStatusCode.Conflict, response);
    }

    /// <summary>A name another role holds is answered with a 500 on edit.</summary>
    [Fact]
    public async Task Edit_answers_a_name_that_is_already_taken_with_a_server_error()
    {
        var role = TestData.SystemRole();
        await Seed(role);

        var response = await RootClient.PutAsJsonAsync($"api/system-roles/{role.Id}", new { name = "Observer" }, Ct);

        var problem = await AssertProblem(HttpStatusCode.InternalServerError, response);

        Assert.Equal("An error occurred while saving the entity changes. See the inner exception for details.", problem.Detail);
    }

    [Fact]
    public async Task Delete_by_a_caller_holding_ManageRoles_removes_the_role()
    {
        var role = TestData.SystemRole();
        await Seed(role);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageRoles).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/system-roles/{role.Id}", Ct);

        await AssertStatus(HttpStatusCode.NoContent, response);
        Assert.Null(await StoredRole(role.Name));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_holding_only_ViewRoles()
    {
        var role = TestData.SystemRole();
        await Seed(role);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewRoles).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/system-roles/{role.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.NotNull(await StoredRole(role.Name));
    }

    [Fact]
    public async Task Delete_of_the_administrator_role_is_a_conflict()
    {
        var response = await RootClient.DeleteAsync($"api/system-roles/{TestData.Roles.Administrator}", Ct);

        await AssertProblem(HttpStatusCode.Conflict, response);
    }

    private async Task<SystemRole> StoredRole(string name)
    {
        await using var context = NewContext();

        return await context.SystemRoles.SingleOrDefaultAsync(x => x.Name == name, Ct);
    }
}
