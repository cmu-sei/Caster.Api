// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;

namespace Caster.Api.Tests.Features.ProjectRoles;

/// <summary>How a project role's permission list is answered.</summary>
public class ProjectRoleMappingTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    /// <summary>A role stored without a permission list is read with an empty one, alone and in the list.</summary>
    [Fact]
    public async Task A_role_stored_without_permissions_is_read_with_an_empty_list()
    {
        var role = TestData.ProjectRole();
        role.Permissions = null;
        var withPermissions = TestData.ProjectRole(permissions: [ProjectPermission.LockFiles, ProjectPermission.ViewProject]);
        await Seed(role, withPermissions);

        var one = await ReadAsync<JsonElement>(await RootClient.GetAsync($"api/project-roles/{role.Id}", Ct));
        var all = await ReadAsync<JsonElement[]>(await RootClient.GetAsync("api/project-roles", Ct));

        Assert.Equal(0, one.GetProperty("permissions").GetArrayLength());
        Assert.Equal((role.Name, false), (one.GetProperty("name").GetString(), one.GetProperty("allPermissions").GetBoolean()));
        Assert.Equal(0, Assert.Single(all, x => x.GetProperty("id").GetGuid() == role.Id).GetProperty("permissions").GetArrayLength());
        Assert.Equal(["LockFiles", "ViewProject"], Assert.Single(all, x => x.GetProperty("id").GetGuid() == withPermissions.Id)
            .GetProperty("permissions").EnumerateArray().Select(x => x.GetString()));
    }
}
