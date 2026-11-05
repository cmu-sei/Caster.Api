// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Hubs;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using PartitionView = Caster.Api.Features.Vlan.Partition;
using PoolView = Caster.Api.Features.Vlan.Pool;
using VlanView = Caster.Api.Features.Vlan.Vlan;
using VlanEntity = Caster.Api.Domain.Models.Vlan;

namespace Caster.Api.Tests.Features.Vlan;

/// <summary>What pools, partitions and VLANs are answered and broadcast with, and what their PUTs and PATCHes store.</summary>
public class VlanMappingTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    [Fact]
    public async Task Reads_answer_every_member()
    {
        var (pool, partition, vlan) = await SeedPool();

        var vlanJson = await ReadAsync<JsonElement>(await RootClient.GetAsync($"api/vlans/{vlan.Id}", Ct));
        var poolJson = await ReadAsync<JsonElement>(await RootClient.GetAsync($"api/vlans/pools/{pool.Id}", Ct));
        var partitionJson = await ReadAsync<JsonElement>(await RootClient.GetAsync($"api/vlans/partitions/{partition.Id}", Ct));

        Assert.Equal((vlan.Id, pool.Id, partition.Id, 7, true, "tagged", true, true), (
            vlanJson.GetProperty("id").GetGuid(), vlanJson.GetProperty("poolId").GetGuid(), vlanJson.GetProperty("partitionId").GetGuid(),
            vlanJson.GetProperty("vlanId").GetInt32(), vlanJson.GetProperty("inUse").GetBoolean(), vlanJson.GetProperty("tag").GetString(),
            vlanJson.GetProperty("reserved").GetBoolean(), vlanJson.GetProperty("reservedEditable").GetBoolean()));
        Assert.Equal((pool.Id, "test-pool", true), (poolJson.GetProperty("id").GetGuid(), poolJson.GetProperty("name").GetString(), poolJson.GetProperty("isDefault").GetBoolean()));
        Assert.Equal((partition.Id, pool.Id, "test-partition", true), (partitionJson.GetProperty("id").GetGuid(), partitionJson.GetProperty("poolId").GetGuid(),
            partitionJson.GetProperty("name").GetString(), partitionJson.GetProperty("isDefault").GetBoolean()));
    }

    /// <summary>A VLAN PATCH keeps what its body leaves out and writes what it sends.</summary>
    [Fact]
    public async Task A_vlan_patch_keeps_what_the_body_leaves_out()
    {
        var (_, _, vlan) = await SeedPool();

        var empty = await ReadAsync<JsonElement>(await RootClient.PatchAsJsonAsync($"api/vlans/{vlan.Id}", new { }, Ct));
        var afterEmpty = await ReadBack(db => db.Vlans.SingleAsync(x => x.Id == vlan.Id, Ct));
        var written = await ReadAsync<JsonElement>(await RootClient.PatchAsJsonAsync($"api/vlans/{vlan.Id}", new { tag = "retagged", reserved = false }, Ct));
        var afterWrite = await ReadBack(db => db.Vlans.SingleAsync(x => x.Id == vlan.Id, Ct));

        Assert.Equal(("tagged", true), (afterEmpty.Tag, afterEmpty.Reserved));
        Assert.Equal("tagged", empty.GetProperty("tag").GetString());
        Assert.Equal(("retagged", false), (afterWrite.Tag, afterWrite.Reserved));
        Assert.Equal("retagged", written.GetProperty("tag").GetString());
        var broadcast = Assert.IsType<VlanView>(Assert.Single(Factory.Hub<ProjectHub>().ToGroup(nameof(HubGroups.VlansAdmin)),
            x => x.Method == "VlanUpdated" && ((VlanView)x.Arguments[0]).Id == vlan.Id).Arguments[0]);
        Assert.Equal("retagged", broadcast.Tag);
    }

    /// <summary>A partition or pool PATCH keeps an omitted name; a PUT without one clears it.</summary>
    [Fact]
    public async Task Partition_and_pool_patches_keep_an_omitted_name_and_puts_clear_it()
    {
        var (pool, partition, _) = await SeedPool();

        var patchedPartition = await ReadAsync<JsonElement>(await RootClient.PatchAsJsonAsync($"api/vlans/partitions/{partition.Id}", new { }, Ct));
        var patchedPool = await ReadAsync<JsonElement>(await RootClient.PatchAsJsonAsync($"api/vlans/pools/{pool.Id}", new { }, Ct));
        Assert.Equal("test-partition", patchedPartition.GetProperty("name").GetString());
        Assert.Equal("test-pool", patchedPool.GetProperty("name").GetString());
        Assert.Equal("test-partition", (await ReadBack(db => db.Partitions.SingleAsync(x => x.Id == partition.Id, Ct))).Name);
        Assert.Equal("test-pool", (await ReadBack(db => db.Pools.SingleAsync(x => x.Id == pool.Id, Ct))).Name);

        var putPartition = await ReadAsync<JsonElement>(await RootClient.PutAsJsonAsync($"api/vlans/partitions/{partition.Id}", new { }, Ct));
        var putPool = await ReadAsync<JsonElement>(await RootClient.PutAsJsonAsync($"api/vlans/pools/{pool.Id}", new { }, Ct));
        Assert.Equal(JsonValueKind.Null, putPartition.GetProperty("name").ValueKind);
        Assert.Equal(JsonValueKind.Null, putPool.GetProperty("name").ValueKind);
        Assert.Null((await ReadBack(db => db.Partitions.SingleAsync(x => x.Id == partition.Id, Ct))).Name);
        Assert.Null((await ReadBack(db => db.Pools.SingleAsync(x => x.Id == pool.Id, Ct))).Name);
        Assert.True((await ReadBack(db => db.Partitions.SingleAsync(x => x.Id == partition.Id, Ct))).IsDefault);
    }

    /// <summary>Creates store the body's members and are broadcast as they are answered.</summary>
    [Fact]
    public async Task Creates_store_the_body_and_are_broadcast()
    {
        var (pool, _, _) = await SeedPool();

        var partition = await ReadAsync<JsonElement>(await RootClient.PostAsJsonAsync($"api/vlans/pools/{pool.Id}/partitions", new { name = "created-partition", vlans = 0 }, Ct));
        var newPool = await ReadAsync<JsonElement>(await RootClient.PostAsJsonAsync("api/vlans/pools", new { name = "created-pool", reservedVlanIds = new int[0] }, Ct));

        Assert.Equal((pool.Id, "created-partition", false), (partition.GetProperty("poolId").GetGuid(), partition.GetProperty("name").GetString(), partition.GetProperty("isDefault").GetBoolean()));
        Assert.Equal("created-pool", newPool.GetProperty("name").GetString());
        var broadcasts = Factory.Hub<ProjectHub>().ToGroup(nameof(HubGroups.VlansAdmin));
        Assert.Contains(broadcasts, x => x.Method == "PartitionCreated" && ((PartitionView)x.Arguments[0]).Name == "created-partition");
        Assert.Contains(broadcasts, x => x.Method == "PoolCreated" && ((PoolView)x.Arguments[0]).Name == "created-pool");
    }

    private async Task<(Pool Pool, Partition Partition, VlanEntity Vlan)> SeedPool()
    {
        var pool = TestData.Pool();
        pool.IsDefault = true;
        var partition = TestData.Partition(pool);
        partition.IsDefault = true;
        var vlan = TestData.Vlan(pool, 7, partition);
        vlan.Tag = "tagged";
        vlan.InUse = true;
        vlan.Reserved = true;
        await Seed(pool, partition, vlan);

        return (pool, partition, vlan);
    }
}
