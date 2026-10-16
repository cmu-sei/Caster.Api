// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;

namespace Caster.Api.Tests.Features.SystemPermissions;

/// <summary><c>GET api/permissions/mine</c>: any signed-in caller reads the system permissions their claims carry.</summary>
public class SystemPermissionRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    [Fact]
    public async Task GetMine_returns_exactly_the_callers_system_permissions()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewHosts, SystemPermission.ManageHosts).SeedAsync();

        var response = await Client(actor).GetAsync("api/permissions/mine", Ct);

        Assert.Equal([SystemPermission.ViewHosts, SystemPermission.ManageHosts], await ReadAsync<SystemPermission[]>(response));
    }

    [Fact]
    public async Task GetMine_returns_nothing_to_a_caller_holding_only_project_permissions()
    {
        var actor = await Actor().OnNewProject(ProjectPermission.ManageProject).SeedAsync();

        var response = await Client(actor).GetAsync("api/permissions/mine", Ct);

        Assert.Empty(await ReadAsync<SystemPermission[]>(response));
    }

    /// <summary>A request with no identity is answered with a 401; the anonymous client is the case under test.</summary>
    [Fact]
    public async Task GetMine_without_an_identity_is_unauthorized()
    {
        await AssertStatus(HttpStatusCode.Unauthorized, await Client().GetAsync("api/permissions/mine", Ct));
    }
}
