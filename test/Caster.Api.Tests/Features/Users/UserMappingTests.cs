// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Tests.Features.Users;

/// <summary>A user's role id is a string in the API and a <c>Guid?</c> in the database: how it is converted both ways.</summary>
public class UserMappingTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    /// <summary>A read answers the role id as a string, and an empty string (not null) for a user without a role.</summary>
    [Fact]
    public async Task A_users_role_id_is_read_as_a_string_or_an_empty_string()
    {
        var withRole = TestData.User(name: "With Role", roleId: TestData.Roles.Observer);
        var withoutRole = TestData.User(name: "Without Role");
        await Seed(withRole, withoutRole);

        var one = await ReadAsync<JsonElement>(await RootClient.GetAsync($"api/users/{withRole.Id}", Ct));
        var none = await ReadAsync<JsonElement>(await RootClient.GetAsync($"api/users/{withoutRole.Id}", Ct));
        var all = await ReadAsync<JsonElement[]>(await RootClient.GetAsync("api/users", Ct));

        Assert.Equal((withRole.Id, "With Role", TestData.Roles.Observer.ToString()),
            (one.GetProperty("id").GetGuid(), one.GetProperty("name").GetString(), one.GetProperty("roleId").GetString()));
        Assert.Equal("", none.GetProperty("roleId").GetString());
        Assert.Equal("", Assert.Single(all, x => x.GetProperty("id").GetGuid() == withoutRole.Id).GetProperty("roleId").GetString());
        Assert.Equal(TestData.Roles.Observer.ToString(), Assert.Single(all, x => x.GetProperty("id").GetGuid() == withRole.Id).GetProperty("roleId").GetString());
    }

    /// <summary>
    /// A create stores the role id it sends, or none, and answers null for none, where a read answers an empty
    /// string. The body's allPermissions is not stored anywhere.
    /// </summary>
    [Fact]
    public async Task Create_stores_the_role_id_of_the_body()
    {
        var id = Guid.NewGuid();
        var noRoleId = Guid.NewGuid();

        var created = await ReadAsync<JsonElement>(await RootClient.PostAsJsonAsync("api/users",
            new { id, name = "Created", allPermissions = true, roleId = TestData.Roles.Observer.ToString() }, Ct));
        var noRole = await ReadAsync<JsonElement>(await RootClient.PostAsJsonAsync("api/users", new { id = noRoleId, name = "No Role" }, Ct));

        Assert.Equal((id, TestData.Roles.Observer.ToString()), (created.GetProperty("id").GetGuid(), created.GetProperty("roleId").GetString()));
        Assert.Equal(JsonValueKind.Null, noRole.GetProperty("roleId").ValueKind);
        await using var context = NewContext();
        Assert.Equal(TestData.Roles.Observer, (await context.Users.SingleAsync(x => x.Id == id, Ct)).RoleId);
        Assert.Null((await context.Users.SingleAsync(x => x.Id == noRoleId, Ct)).RoleId);
    }

    /// <summary>A role id that is not a Guid (an empty string included) is answered with a 500, and nothing is stored.</summary>
    [Fact]
    public async Task A_role_id_that_is_not_a_guid_is_a_server_error()
    {
        var id = Guid.NewGuid();

        await AssertProblem(HttpStatusCode.InternalServerError, await RootClient.PostAsJsonAsync("api/users", new { id, name = "Bad", roleId = "" }, Ct));
        await AssertProblem(HttpStatusCode.InternalServerError, await RootClient.PostAsJsonAsync("api/users", new { id, name = "Bad", roleId = "not-a-guid" }, Ct));

        Assert.False(await ReadBack(db => db.Users.AnyAsync(x => x.Id == id, Ct)));
    }

    /// <summary>A PUT writes every member: one without a role id clears the user's role.</summary>
    [Fact]
    public async Task Put_without_a_role_id_clears_the_role()
    {
        var user = TestData.User(name: "Before", roleId: TestData.Roles.Observer);
        await Seed(user);

        var put = await ReadAsync<JsonElement>(await RootClient.PutAsJsonAsync($"api/users/{user.Id}", new { id = user.Id, name = "After" }, Ct));

        Assert.Equal(("After", JsonValueKind.Null), (put.GetProperty("name").GetString(), put.GetProperty("roleId").ValueKind));
        var stored = await ReadBack(db => db.Users.SingleAsync(x => x.Id == user.Id, Ct));
        Assert.Equal(("After", (Guid?)null), (stored.Name, stored.RoleId));
    }
}
