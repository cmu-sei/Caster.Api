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
using HostView = Caster.Api.Features.Hosts.Host;

namespace Caster.Api.Tests.Features.Hosts;

/// <summary><c>api/hosts</c>: reading needs ViewHosts, every write ManageHosts.</summary>
public class HostRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    [Fact]
    public async Task Get_returns_the_host_to_a_caller_holding_ViewHosts()
    {
        var host = await SeedHost();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewHosts).SeedAsync();

        var response = await Client(actor).GetAsync($"api/hosts/{host.Id}", Ct);

        Assert.Equal(host.Name, (await ReadAsync<HostView>(response)).Name);
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_only_ManageHosts()
    {
        var host = await SeedHost();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageHosts).SeedAsync();

        var response = await Client(actor).GetAsync($"api/hosts/{host.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task GetAll_lists_the_hosts_for_a_caller_holding_ViewHosts()
    {
        var host = await SeedHost();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewHosts).SeedAsync();

        var response = await Client(actor).GetAsync("api/hosts", Ct);

        Assert.Equal([host.Id], (await ReadAsync<HostView[]>(response)).Select(x => x.Id));
    }

    [Fact]
    public async Task GetAll_is_forbidden_for_a_caller_holding_only_ViewWorkspaces()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewWorkspaces).SeedAsync();

        var response = await Client(actor).GetAsync("api/hosts", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Create_by_a_caller_holding_ManageHosts_stores_the_host()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageHosts).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(
            "api/hosts", new { name = "esx-01", datastore = "ds1", maximumMachines = 4, enabled = true }, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        await using var context = NewContext();
        var stored = await context.Hosts.SingleAsync(x => x.Name == "esx-01", Ct);
        Assert.Equal(4, stored.MaximumMachines);
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_caller_holding_only_ViewHosts()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewHosts).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/hosts", new { name = "esx-02" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        await using var context = NewContext();
        Assert.False(await context.Hosts.AnyAsync(x => x.Name == "esx-02", Ct));
    }

    [Fact]
    public async Task Edit_by_a_caller_holding_ManageHosts_stores_the_change()
    {
        var host = await SeedHost();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageHosts).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync(
            $"api/hosts/{host.Id}", new { name = host.Name, datastore = "ds2", maximumMachines = 20, enabled = false }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(20, (await StoredHost(host.Id)).MaximumMachines);
    }

    [Fact]
    public async Task Edit_is_forbidden_for_a_caller_holding_only_ViewHosts()
    {
        var host = await SeedHost();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewHosts).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync(
            $"api/hosts/{host.Id}", new { name = host.Name, maximumMachines = 20 }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(10, (await StoredHost(host.Id)).MaximumMachines);
    }

    [Fact]
    public async Task Delete_by_a_caller_holding_ManageHosts_removes_the_host()
    {
        var host = await SeedHost();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageHosts).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/hosts/{host.Id}", Ct);

        await AssertStatus(HttpStatusCode.NoContent, response);
        Assert.Null(await StoredHost(host.Id));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_holding_only_ViewHosts()
    {
        var host = await SeedHost();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewHosts).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/hosts/{host.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.NotNull(await StoredHost(host.Id));
    }

    private async Task<Host> SeedHost()
    {
        var host = TestData.Host();
        await Seed(host);

        return host;
    }

    private async Task<Host> StoredHost(Guid id)
    {
        await using var context = NewContext();

        return await context.Hosts.SingleOrDefaultAsync(x => x.Id == id, Ct);
    }
}
