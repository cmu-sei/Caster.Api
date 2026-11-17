// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Tests.Features.SystemRoles;

/// <summary>How a system role's permission list is stored and answered when it is missing.</summary>
public class SystemRoleMappingTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    /// <summary>A role stored without a permission list is read with an empty one, alone and in the list.</summary>
    [Fact]
    public async Task A_role_stored_without_permissions_is_read_with_an_empty_list()
    {
        var role = TestData.SystemRole();
        role.Permissions = null;
        await Seed(role);

        var one = await ReadAsync<JsonElement>(await RootClient.GetAsync($"api/system-roles/{role.Id}", Ct));
        var all = await ReadAsync<JsonElement[]>(await RootClient.GetAsync("api/system-roles", Ct));

        Assert.Equal(0, one.GetProperty("permissions").GetArrayLength());
        Assert.Equal(0, Assert.Single(all, x => x.GetProperty("id").GetGuid() == role.Id).GetProperty("permissions").GetArrayLength());
        Assert.Equal((role.Name, false, false), (one.GetProperty("name").GetString(), one.GetProperty("allPermissions").GetBoolean(), one.GetProperty("immutable").GetBoolean()));
    }

    /// <summary>A create or PUT without permissions stores and answers an empty list.</summary>
    [Fact]
    public async Task A_role_written_without_permissions_stores_an_empty_list()
    {
        var role = TestData.SystemRole(permissions: [SystemPermission.ViewUsers]);
        await Seed(role);

        var created = await ReadAsync<JsonElement>(await RootClient.PostAsJsonAsync("api/system-roles", new { name = $"created-{Guid.NewGuid():N}", allPermissions = true }, Ct));
        var put = await ReadAsync<JsonElement>(await RootClient.PutAsJsonAsync($"api/system-roles/{role.Id}", new { id = role.Id, name = role.Name }, Ct));

        Assert.Equal(0, created.GetProperty("permissions").GetArrayLength());
        Assert.True(created.GetProperty("allPermissions").GetBoolean());
        Assert.Equal(0, put.GetProperty("permissions").GetArrayLength());
        var createdId = created.GetProperty("id").GetGuid();
        await using var context = NewContext();
        var storedCreated = await context.SystemRoles.SingleAsync(x => x.Id == createdId, Ct);
        var storedPut = await context.SystemRoles.SingleAsync(x => x.Id == role.Id, Ct);
        Assert.NotNull(storedCreated.Permissions);
        Assert.Empty(storedCreated.Permissions);
        Assert.NotNull(storedPut.Permissions);
        Assert.Empty(storedPut.Permissions);
    }

    /// <summary>The permissions a body sends are stored and answered.</summary>
    [Fact]
    public async Task A_role_written_with_permissions_stores_them()
    {
        var role = TestData.SystemRole();
        await Seed(role);

        var put = await ReadAsync<JsonElement>(await RootClient.PutAsJsonAsync($"api/system-roles/{role.Id}",
            new { id = role.Id, name = role.Name, permissions = new[] { "ViewUsers", "ManageGroups" } }, Ct));

        Assert.Equal(["ViewUsers", "ManageGroups"], put.GetProperty("permissions").EnumerateArray().Select(x => x.GetString()));
        var stored = await ReadBack(db => db.SystemRoles.SingleAsync(x => x.Id == role.Id, Ct));
        Assert.Equal([SystemPermission.ViewUsers, SystemPermission.ManageGroups], stored.Permissions);
    }
}
