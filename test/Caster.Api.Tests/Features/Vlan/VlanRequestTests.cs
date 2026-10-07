// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Hubs;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using PartitionView = Caster.Api.Features.Vlan.Partition;
using PoolView = Caster.Api.Features.Vlan.Pool;
using VlanEntity = Caster.Api.Domain.Models.Vlan;
using VlanView = Caster.Api.Features.Vlan.Vlan;

namespace Caster.Api.Tests.Features.Vlan;

/// <summary>
/// <c>VlansController</c>: every read needs ViewVLANs and every write ManageVLANs, with no project path.
/// The near miss of a read is ManageVLANs alone, of a write ViewVLANs.
/// </summary>
public class VlanRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    // ---- pools ----------------------------------------------------------------------------------

    [Fact]
    public async Task CreatePool_by_a_caller_holding_ManageVLANs_stores_every_vlan_id_with_the_reserved_ones_marked()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageVLANs).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/vlans/pools", new { name = "lab", reservedVlanIds = new[] { 0, 100 } }, Ct);

        var created = await ReadAsync<PoolView>(response);
        await using var context = NewContext();
        Assert.Equal(4096, await context.Vlans.CountAsync(x => x.PoolId == created.Id, Ct));
        Assert.Equal(new[] { 0, 100 }, await context.Vlans.Where(x => x.PoolId == created.Id && x.Reserved).Select(x => x.VlanId).OrderBy(x => x).ToArrayAsync(Ct));
    }

    [Fact]
    public async Task CreatePool_is_forbidden_for_a_caller_holding_only_ViewVLANs()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewVLANs).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/vlans/pools", new { name = "refused" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        await using var context = NewContext();
        Assert.False(await context.Pools.AnyAsync(x => x.Name == "refused", Ct));
    }

    [Fact]
    public async Task GetPools_lists_the_pools_for_a_caller_holding_ViewVLANs()
    {
        var (pool, _) = await SeedPartition();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewVLANs).SeedAsync();

        Assert.Equal([pool.Id], (await ReadAsync<PoolView[]>(await Client(actor).GetAsync("api/vlans/pools", Ct))).Select(x => x.Id));
    }

    [Fact]
    public async Task GetPools_is_forbidden_for_a_caller_holding_only_ManageVLANs()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageVLANs).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync("api/vlans/pools", Ct));
    }

    [Fact]
    public async Task GetPool_returns_the_pool_to_a_caller_holding_ViewVLANs()
    {
        var (pool, _) = await SeedPartition();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewVLANs).SeedAsync();

        Assert.Equal(pool.Name, (await ReadAsync<PoolView>(await Client(actor).GetAsync($"api/vlans/pools/{pool.Id}", Ct))).Name);
    }

    [Fact]
    public async Task GetPool_is_forbidden_for_a_caller_holding_only_ManageVLANs()
    {
        var (pool, _) = await SeedPartition();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageVLANs).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/vlans/pools/{pool.Id}", Ct));
    }

    [Fact]
    public async Task EditPool_by_a_caller_holding_ManageVLANs_stores_the_new_name()
    {
        var (pool, _) = await SeedPartition();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageVLANs).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).PutAsJsonAsync($"api/vlans/pools/{pool.Id}", new { name = "renamed" }, Ct));

        Assert.Equal("renamed", (await StoredPool(pool.Id)).Name);
    }

    [Fact]
    public async Task EditPool_is_forbidden_for_a_caller_holding_only_ViewVLANs()
    {
        var (pool, _) = await SeedPartition();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewVLANs).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PutAsJsonAsync($"api/vlans/pools/{pool.Id}", new { name = "renamed" }, Ct));
        Assert.Equal(pool.Name, (await StoredPool(pool.Id)).Name);
    }

    [Fact]
    public async Task PartialEditPool_by_a_caller_holding_ManageVLANs_stores_the_new_name()
    {
        var (pool, _) = await SeedPartition();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageVLANs).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).PatchAsJsonAsync($"api/vlans/pools/{pool.Id}", new { name = "patched" }, Ct));

        Assert.Equal("patched", (await StoredPool(pool.Id)).Name);
    }

    [Fact]
    public async Task PartialEditPool_is_forbidden_for_a_caller_holding_only_ViewVLANs()
    {
        var (pool, _) = await SeedPartition();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewVLANs).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PatchAsJsonAsync($"api/vlans/pools/{pool.Id}", new { name = "patched" }, Ct));
    }

    [Fact]
    public async Task DeletePool_by_a_caller_holding_ManageVLANs_removes_the_pool()
    {
        var (pool, _) = await SeedPartition();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageVLANs).SeedAsync();

        await AssertStatus(HttpStatusCode.NoContent, await Client(actor).SendAsync(DeletePool(pool.Id, force: false), Ct));

        Assert.Null(await StoredPool(pool.Id));
    }

    [Fact]
    public async Task DeletePool_is_forbidden_for_a_caller_holding_only_ViewVLANs()
    {
        var (pool, _) = await SeedPartition();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewVLANs).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).SendAsync(DeletePool(pool.Id, force: false), Ct));
        Assert.NotNull(await StoredPool(pool.Id));
    }

    [Fact]
    public async Task DeletePool_with_a_vlan_in_use_is_a_conflict()
    {
        var (pool, partition) = await SeedPartition();
        var vlan = TestData.Vlan(pool, 10, partition);
        vlan.InUse = true;
        await Seed(vlan);

        await AssertProblem(HttpStatusCode.Conflict, await RootClient.SendAsync(DeletePool(pool.Id, force: false), Ct));
        Assert.NotNull(await StoredPool(pool.Id));
    }

    // ---- partitions -----------------------------------------------------------------------------

    [Fact]
    public async Task CreatePartition_by_a_caller_holding_ManageVLANs_takes_the_lowest_free_vlans_of_the_pool()
    {
        var pool = TestData.Pool();
        await Seed(pool, TestData.Vlan(pool, 5), TestData.Vlan(pool, 3), TestData.Vlan(pool, 9));
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageVLANs).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/vlans/pools/{pool.Id}/partitions", new { name = "event", vlans = 2 }, Ct);

        var created = await ReadAsync<PartitionView>(response);
        await using var context = NewContext();
        Assert.Equal(new[] { 3, 5 }, await context.Vlans.Where(x => x.PartitionId == created.Id).Select(x => x.VlanId).OrderBy(x => x).ToArrayAsync(Ct));
    }

    [Fact]
    public async Task CreatePartition_is_forbidden_for_a_caller_holding_only_ViewVLANs()
    {
        var pool = TestData.Pool();
        await Seed(pool);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewVLANs).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/vlans/pools/{pool.Id}/partitions", new { name = "event", vlans = 0 }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task CreatePartition_asking_for_more_vlans_than_are_free_is_a_conflict()
    {
        var pool = TestData.Pool();
        await Seed(pool, TestData.Vlan(pool, 1));

        var response = await RootClient.PostAsJsonAsync($"api/vlans/pools/{pool.Id}/partitions", new { name = "event", vlans = 2 }, Ct);

        await AssertProblem(HttpStatusCode.Conflict, response);
    }

    [Fact]
    public async Task GetPartitions_lists_every_partition_for_a_caller_holding_ViewVLANs()
    {
        var (_, partition) = await SeedPartition();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewVLANs).SeedAsync();

        Assert.Equal([partition.Id], (await ReadAsync<PartitionView[]>(await Client(actor).GetAsync("api/vlans/partitions", Ct))).Select(x => x.Id));
    }

    [Fact]
    public async Task GetPartitions_is_forbidden_for_a_caller_holding_only_ManageVLANs()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageVLANs).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync("api/vlans/partitions", Ct));
    }

    [Fact]
    public async Task GetPartitionsByPool_lists_the_pools_partitions_for_a_caller_holding_ViewVLANs()
    {
        var (pool, partition) = await SeedPartition();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewVLANs).SeedAsync();

        var response = await Client(actor).GetAsync($"api/vlans/pools/{pool.Id}/partitions", Ct);

        Assert.Equal([partition.Id], (await ReadAsync<PartitionView[]>(response)).Select(x => x.Id));
    }

    [Fact]
    public async Task GetPartition_returns_the_partition_to_a_caller_holding_ViewVLANs()
    {
        var (_, partition) = await SeedPartition();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewVLANs).SeedAsync();

        Assert.Equal(partition.Name, (await ReadAsync<PartitionView>(await Client(actor).GetAsync($"api/vlans/partitions/{partition.Id}", Ct))).Name);
    }

    [Fact]
    public async Task GetPartition_is_forbidden_for_a_caller_holding_only_ManageVLANs()
    {
        var (_, partition) = await SeedPartition();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageVLANs).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/vlans/partitions/{partition.Id}", Ct));
    }

    [Fact]
    public async Task EditPartition_by_a_caller_holding_ManageVLANs_stores_the_new_name()
    {
        var (_, partition) = await SeedPartition();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageVLANs).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).PutAsJsonAsync($"api/vlans/partitions/{partition.Id}", new { name = "renamed" }, Ct));

        Assert.Equal("renamed", (await StoredPartition(partition.Id)).Name);
    }

    [Fact]
    public async Task EditPartition_is_forbidden_for_a_caller_holding_only_ViewVLANs()
    {
        var (_, partition) = await SeedPartition();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewVLANs).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PutAsJsonAsync($"api/vlans/partitions/{partition.Id}", new { name = "renamed" }, Ct));
        Assert.Equal(partition.Name, (await StoredPartition(partition.Id)).Name);
    }

    [Fact]
    public async Task PartialEditPartition_by_a_caller_holding_ManageVLANs_stores_the_new_name()
    {
        var (_, partition) = await SeedPartition();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageVLANs).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).PatchAsJsonAsync($"api/vlans/partitions/{partition.Id}", new { name = "patched" }, Ct));
    }

    [Fact]
    public async Task PartialEditPartition_is_forbidden_for_a_caller_holding_only_ViewVLANs()
    {
        var (_, partition) = await SeedPartition();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewVLANs).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PatchAsJsonAsync($"api/vlans/partitions/{partition.Id}", new { name = "patched" }, Ct));
    }

    [Fact]
    public async Task SetDefaultPartition_by_a_caller_holding_ManageVLANs_makes_it_and_its_pool_the_default()
    {
        var (pool, partition) = await SeedPartition();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageVLANs).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).PostAsync($"api/vlans/partitions/{partition.Id}/actions/set-default", null, Ct));

        Assert.True((await StoredPartition(partition.Id)).IsDefault);
        Assert.True((await StoredPool(pool.Id)).IsDefault);
    }

    [Fact]
    public async Task SetDefaultPartition_is_forbidden_for_a_caller_holding_only_ViewVLANs()
    {
        var (_, partition) = await SeedPartition();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewVLANs).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsync($"api/vlans/partitions/{partition.Id}/actions/set-default", null, Ct));
        Assert.False((await StoredPartition(partition.Id)).IsDefault);
    }

    [Fact]
    public async Task UnsetDefaultPartition_by_a_caller_holding_ManageVLANs_clears_the_default()
    {
        var (_, partition) = await SeedPartition(isDefault: true);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageVLANs).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).PostAsync("api/vlans/partitions/actions/unset-default", null, Ct));

        Assert.False((await StoredPartition(partition.Id)).IsDefault);
    }

    [Fact]
    public async Task UnsetDefaultPartition_is_forbidden_for_a_caller_holding_only_ViewVLANs()
    {
        var (_, partition) = await SeedPartition(isDefault: true);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewVLANs).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsync("api/vlans/partitions/actions/unset-default", null, Ct));
        Assert.True((await StoredPartition(partition.Id)).IsDefault);
    }

    [Fact]
    public async Task DeletePartition_by_a_caller_holding_ManageVLANs_removes_the_partition()
    {
        var (_, partition) = await SeedPartition();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageVLANs).SeedAsync();

        await AssertStatus(HttpStatusCode.NoContent, await Client(actor).DeleteAsync($"api/vlans/partitions/{partition.Id}", Ct));

        Assert.Null(await StoredPartition(partition.Id));
    }

    [Fact]
    public async Task DeletePartition_is_forbidden_for_a_caller_holding_only_ViewVLANs()
    {
        var (_, partition) = await SeedPartition();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewVLANs).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).DeleteAsync($"api/vlans/partitions/{partition.Id}", Ct));
        Assert.NotNull(await StoredPartition(partition.Id));
    }

    [Fact]
    public async Task AssignPartition_by_a_caller_holding_ManageVLANs_stores_it_on_the_project()
    {
        var (_, partition) = await SeedPartition();
        var project = TestData.Project();
        await Seed(project);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageVLANs).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).PostAsJsonAsync($"api/vlans/partitions/{partition.Id}/actions/assign", new { projectId = project.Id }, Ct));

        Assert.Equal(partition.Id, (await StoredProject(project.Id)).PartitionId);
    }

    /// <summary>Assigning a partition has no project path: the project's own manager is refused.</summary>
    [Fact]
    public async Task AssignPartition_is_forbidden_for_a_caller_holding_ManageProject_on_the_project()
    {
        var (_, partition) = await SeedPartition();
        var project = TestData.Project();
        await Seed(project);
        var actor = await Actor().OnProject(project, roleId: TestData.ProjectRoles.Manager).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/vlans/partitions/{partition.Id}/actions/assign", new { projectId = project.Id }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Null((await StoredProject(project.Id)).PartitionId);
    }

    [Fact]
    public async Task UnassignPartition_by_a_caller_holding_ManageVLANs_clears_it_from_the_project()
    {
        var (_, partition) = await SeedPartition();
        var project = TestData.Project();
        project.PartitionId = partition.Id;
        await Seed(project);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageVLANs).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).PostAsJsonAsync("api/vlans/partitions/actions/unassign", new { projectId = project.Id }, Ct));

        Assert.Null((await StoredProject(project.Id)).PartitionId);
    }

    [Fact]
    public async Task UnassignPartition_is_forbidden_for_a_caller_holding_only_ViewVLANs()
    {
        var (_, partition) = await SeedPartition();
        var project = TestData.Project();
        project.PartitionId = partition.Id;
        await Seed(project);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewVLANs).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsJsonAsync("api/vlans/partitions/actions/unassign", new { projectId = project.Id }, Ct));
        Assert.Equal(partition.Id, (await StoredProject(project.Id)).PartitionId);
    }

    // ---- vlans ----------------------------------------------------------------------------------

    [Fact]
    public async Task AddVlansToPartition_by_a_caller_holding_ManageVLANs_moves_unassigned_vlans_into_it()
    {
        var (pool, partition) = await SeedPartition();
        var free = TestData.Vlan(pool, 20);
        await Seed(free);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageVLANs).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).PostAsJsonAsync($"api/vlans/partitions/{partition.Id}/actions/vlans/add", new { vlans = 1 }, Ct));

        Assert.Equal(partition.Id, (await StoredVlan(free.Id)).PartitionId);
    }

    [Fact]
    public async Task AddVlansToPartition_is_forbidden_for_a_caller_holding_only_ViewVLANs()
    {
        var (pool, partition) = await SeedPartition();
        var free = TestData.Vlan(pool, 20);
        await Seed(free);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewVLANs).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsJsonAsync($"api/vlans/partitions/{partition.Id}/actions/vlans/add", new { vlans = 1 }, Ct));
        Assert.Null((await StoredVlan(free.Id)).PartitionId);
    }

    [Fact]
    public async Task RemoveVlansFromPartition_by_a_caller_holding_ManageVLANs_returns_them_to_the_pool()
    {
        var (pool, partition) = await SeedPartition();
        var vlan = TestData.Vlan(pool, 30, partition);
        await Seed(vlan);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageVLANs).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).PostAsJsonAsync($"api/vlans/partitions/{partition.Id}/actions/vlans/remove", new { vlanIds = new[] { vlan.Id } }, Ct));

        Assert.Null((await StoredVlan(vlan.Id)).PartitionId);
    }

    [Fact]
    public async Task RemoveVlansFromPartition_is_forbidden_for_a_caller_holding_only_ViewVLANs()
    {
        var (pool, partition) = await SeedPartition();
        var vlan = TestData.Vlan(pool, 30, partition);
        await Seed(vlan);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewVLANs).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsJsonAsync($"api/vlans/partitions/{partition.Id}/actions/vlans/remove", new { vlanIds = new[] { vlan.Id } }, Ct));
        Assert.Equal(partition.Id, (await StoredVlan(vlan.Id)).PartitionId);
    }

    [Fact]
    public async Task AcquireVlan_by_a_caller_holding_ManageVLANs_marks_the_lowest_free_vlan_of_the_partition_in_use()
    {
        var (pool, partition) = await SeedPartition();
        var low = TestData.Vlan(pool, 40, partition);
        var high = TestData.Vlan(pool, 41, partition);
        await Seed(low, high);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageVLANs).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/vlans/actions/acquire", new { partitionId = partition.Id }, Ct);

        Assert.Equal(40, (await ReadAsync<VlanView>(response)).VlanId);
        Assert.True((await StoredVlan(low.Id)).InUse);
        Assert.False((await StoredVlan(high.Id)).InUse);
    }

    [Fact]
    public async Task AcquireVlan_is_forbidden_for_a_caller_holding_only_ViewVLANs()
    {
        var (pool, partition) = await SeedPartition();
        var vlan = TestData.Vlan(pool, 40, partition);
        await Seed(vlan);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewVLANs).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsJsonAsync("api/vlans/actions/acquire", new { partitionId = partition.Id }, Ct));
        Assert.False((await StoredVlan(vlan.Id)).InUse);
    }

    /// <summary>Acquiring with no partition named and no default partition is answered with a 409 that no VLANs are available.</summary>
    [Fact]
    public async Task AcquireVlan_with_no_partition_and_no_default_answers_that_no_vlans_are_available()
    {
        var (pool, partition) = await SeedPartition();
        await Seed(TestData.Vlan(pool, 45, partition));

        var response = await RootClient.PostAsJsonAsync("api/vlans/actions/acquire", new { }, Ct);

        var problem = await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.Equal("No VLANs available", problem.Title);
    }

    [Fact]
    public async Task ReleaseVlan_by_a_caller_holding_ManageVLANs_frees_the_vlan()
    {
        var (pool, partition) = await SeedPartition();
        var vlan = TestData.Vlan(pool, 50, partition);
        vlan.InUse = true;
        await Seed(vlan);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageVLANs).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).PostAsync($"api/vlans/{vlan.Id}/actions/release", null, Ct));

        Assert.False((await StoredVlan(vlan.Id)).InUse);
    }

    [Fact]
    public async Task ReleaseVlan_is_forbidden_for_a_caller_holding_only_ViewVLANs()
    {
        var (pool, partition) = await SeedPartition();
        var vlan = TestData.Vlan(pool, 50, partition);
        vlan.InUse = true;
        await Seed(vlan);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewVLANs).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsync($"api/vlans/{vlan.Id}/actions/release", null, Ct));
        Assert.True((await StoredVlan(vlan.Id)).InUse);
    }

    [Fact]
    public async Task GetVlan_returns_the_vlan_to_a_caller_holding_ViewVLANs()
    {
        var (pool, _) = await SeedPartition();
        var vlan = TestData.Vlan(pool, 60);
        await Seed(vlan);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewVLANs).SeedAsync();

        Assert.Equal(60, (await ReadAsync<VlanView>(await Client(actor).GetAsync($"api/vlans/{vlan.Id}", Ct))).VlanId);
    }

    [Fact]
    public async Task GetVlan_is_forbidden_for_a_caller_holding_only_ManageVLANs()
    {
        var (pool, _) = await SeedPartition();
        var vlan = TestData.Vlan(pool, 60);
        await Seed(vlan);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageVLANs).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/vlans/{vlan.Id}", Ct));
    }

    [Fact]
    public async Task GetVlansByPool_lists_the_pools_vlans_for_a_caller_holding_ViewVLANs()
    {
        var (pool, _) = await SeedPartition();
        var vlan = TestData.Vlan(pool, 61);
        await Seed(vlan);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewVLANs).SeedAsync();

        Assert.Equal([vlan.Id], (await ReadAsync<VlanView[]>(await Client(actor).GetAsync($"api/vlans/pools/{pool.Id}/vlans", Ct))).Select(x => x.Id));
    }

    [Fact]
    public async Task GetVlansByPartition_lists_the_partitions_vlans_for_a_caller_holding_ViewVLANs()
    {
        var (pool, partition) = await SeedPartition();
        var vlan = TestData.Vlan(pool, 62, partition);
        await Seed(vlan, TestData.Vlan(pool, 63));
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewVLANs).SeedAsync();

        Assert.Equal([vlan.Id], (await ReadAsync<VlanView[]>(await Client(actor).GetAsync($"api/vlans/partitions/{partition.Id}/vlans", Ct))).Select(x => x.Id));
    }

    [Fact]
    public async Task GetVlansByPartition_is_forbidden_for_a_caller_holding_only_ManageVLANs()
    {
        var (_, partition) = await SeedPartition();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageVLANs).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/vlans/partitions/{partition.Id}/vlans", Ct));
    }

    [Fact]
    public async Task PartialEditVlan_by_a_caller_holding_ManageVLANs_stores_the_tag()
    {
        var (pool, _) = await SeedPartition();
        var vlan = TestData.Vlan(pool, 70);
        await Seed(vlan);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageVLANs).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).PatchAsJsonAsync($"api/vlans/{vlan.Id}", new { tag = "red" }, Ct));

        Assert.Equal("red", (await StoredVlan(vlan.Id)).Tag);
    }

    [Fact]
    public async Task PartialEditVlan_is_forbidden_for_a_caller_holding_only_ViewVLANs()
    {
        var (pool, _) = await SeedPartition();
        var vlan = TestData.Vlan(pool, 70);
        await Seed(vlan);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewVLANs).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PatchAsJsonAsync($"api/vlans/{vlan.Id}", new { tag = "red" }, Ct));
        Assert.Null((await StoredVlan(vlan.Id)).Tag);
    }

    [Fact]
    public async Task ReassignVlans_by_a_caller_holding_ManageVLANs_moves_them_to_the_other_partition()
    {
        var (pool, from) = await SeedPartition();
        var to = TestData.Partition(pool, "to");
        var vlan = TestData.Vlan(pool, 80, from);
        await Seed(to, vlan);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageVLANs).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(
            "api/vlans/actions/reassign-vlans", new { fromPartitionId = from.Id, toPartitionId = to.Id, vlanIds = new[] { vlan.Id } }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(to.Id, (await StoredVlan(vlan.Id)).PartitionId);
    }

    [Fact]
    public async Task ReassignVlans_is_forbidden_for_a_caller_holding_only_ViewVLANs()
    {
        var (pool, from) = await SeedPartition();
        var to = TestData.Partition(pool, "to");
        var vlan = TestData.Vlan(pool, 80, from);
        await Seed(to, vlan);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewVLANs).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(
            "api/vlans/actions/reassign-vlans", new { fromPartitionId = from.Id, toPartitionId = to.Id, vlanIds = new[] { vlan.Id } }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(from.Id, (await StoredVlan(vlan.Id)).PartitionId);
    }

    [Fact]
    public async Task ReserveVlans_by_a_caller_holding_ManageVLANs_marks_them_reserved()
    {
        var (pool, _) = await SeedPartition();
        var vlan = TestData.Vlan(pool, 90);
        await Seed(vlan);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageVLANs).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).PostAsJsonAsync("api/vlans/actions/reserve-vlans", new { reserved = true, vlanIds = new[] { vlan.Id } }, Ct));

        Assert.True((await StoredVlan(vlan.Id)).Reserved);
    }

    [Fact]
    public async Task ReserveVlans_is_forbidden_for_a_caller_holding_only_ViewVLANs()
    {
        var (pool, _) = await SeedPartition();
        var vlan = TestData.Vlan(pool, 90);
        await Seed(vlan);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewVLANs).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsJsonAsync("api/vlans/actions/reserve-vlans", new { reserved = true, vlanIds = new[] { vlan.Id } }, Ct));
        Assert.False((await StoredVlan(vlan.Id)).Reserved);
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static HttpRequestMessage DeletePool(Guid poolId, bool force) =>
        new(HttpMethod.Delete, $"api/vlans/pools/{poolId}") { Content = JsonContent.Create(new { force }) };

    private async Task<(Pool Pool, Partition Partition)> SeedPartition(bool isDefault = false)
    {
        var pool = TestData.Pool();
        var partition = TestData.Partition(pool);
        pool.IsDefault = isDefault;
        partition.IsDefault = isDefault;
        await Seed(pool, partition);

        return (pool, partition);
    }

    private async Task<Pool> StoredPool(Guid id)
    {
        await using var context = NewContext();

        return await context.Pools.SingleOrDefaultAsync(x => x.Id == id, Ct);
    }

    private async Task<Partition> StoredPartition(Guid id)
    {
        await using var context = NewContext();

        return await context.Partitions.SingleOrDefaultAsync(x => x.Id == id, Ct);
    }

    private async Task<VlanEntity> StoredVlan(Guid id)
    {
        await using var context = NewContext();

        return await context.Vlans.SingleAsync(x => x.Id == id, Ct);
    }

    private async Task<Project> StoredProject(Guid id)
    {
        await using var context = NewContext();

        return await context.Projects.SingleAsync(x => x.Id == id, Ct);
    }
}
