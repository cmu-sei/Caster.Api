// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Caster.Api.Domain.Services.Capacity;
using Caster.Api.Domain.Services.Inventory;
using Caster.Api.Infrastructure.Options;

namespace Caster.Api.Tests.Unit.Capacity;

[Trait("Category", "Unit")]
public class HypervisorRoomReaderTests
{
    private const long GiB = 1024L * 1024 * 1024;

    private static InventoryEntry Entry(string id, params (string Key, object Value)[] properties) => new()
    {
        Id = id,
        Name = id,
        Properties = properties.ToDictionary(x => x.Key, x => Convert.ToString(x.Value, System.Globalization.CultureInfo.InvariantCulture)),
    };

    private static InventorySnapshot Snapshot(InventoryEntry[] hosts = null, InventoryEntry[] datastores = null) => new()
    {
        LastUpdated = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc),
        Hosts = hosts == null ? InventoryCategoryResult.Unavailable("no") : InventoryCategoryResult.Ok(hosts),
        Datastores = datastores == null ? InventoryCategoryResult.Unavailable("no") : InventoryCategoryResult.Ok(datastores),
    };

    private static HypervisorRoom Read(string provider, InventorySnapshot snapshot, CapacityOptions options = null) =>
        HypervisorRoomReader.Read([("lab", provider, snapshot)], options ?? new CapacityOptions());

    [Fact]
    public void Free_vcpu_is_cores_times_the_overcommit_ratio_less_what_running_vms_are_given()
    {
        var room = Read("proxmox", Snapshot([Entry("pve1", ("status", "online"), ("maxcpu", 16), ("allocatedCpus", 40), ("cpu", 0.9), ("maxmem", 64 * GiB), ("mem", 20 * GiB))]));

        var host = Assert.Single(room.Hosts);
        Assert.Equal(16 * 4 - 40, host.FreeVcpu);
        Assert.False(host.CpuApproximated);
    }

    [Fact]
    public void Free_memory_is_memory_less_the_reserve_less_what_is_in_use()
    {
        var room = Read("proxmox", Snapshot([Entry("pve1", ("maxcpu", 16), ("allocatedCpus", 0), ("maxmem", 64 * GiB), ("mem", 20 * GiB))]),
            new CapacityOptions { ReservePercent = 25 });

        // 64 GiB less 25% is 48 GiB, less 20 GiB in use.
        Assert.Equal(28 * 1024, Assert.Single(room.Hosts).FreeMemoryMiB);
    }

    [Fact]
    public void Without_allocations_the_current_load_is_taken_off_and_said_so()
    {
        var room = Read("proxmox", Snapshot([Entry("pve1", ("maxcpu", 8), ("cpu", 0.3), ("maxmem", 16 * GiB), ("mem", 0))]),
            new CapacityOptions { CpuOvercommitRatio = 2 });

        var host = Assert.Single(room.Hosts);
        Assert.True(host.CpuApproximated);
        // 0.3 of 8 cores is 2.4 in use, rounded up to 3.
        Assert.Equal(3, host.AllocatedVcpu);
        Assert.Equal(16 - 3, host.FreeVcpu);
    }

    [Fact]
    public void A_host_never_has_less_than_no_room()
    {
        var room = Read("proxmox", Snapshot([Entry("pve1", ("maxcpu", 4), ("allocatedCpus", 100), ("maxmem", 8 * GiB), ("mem", 8 * GiB))]));

        var host = Assert.Single(room.Hosts);
        Assert.Equal(0, host.FreeVcpu);
        Assert.Equal(0, host.FreeMemoryMiB);
    }

    [Theory]
    [InlineData("proxmox", "status", "offline")]
    [InlineData("vsphere", "connectionState", "disconnected")]
    [InlineData("vsphere", "powerState", "standBy")]
    public void An_offline_host_has_no_room(string provider, string key, string value)
    {
        var room = Read(provider, Snapshot([Entry("h", (key, value), ("maxcpu", 16), ("allocatedCpus", 0), ("maxmem", 64 * GiB), ("mem", 0))]));

        var host = Assert.Single(room.Hosts);
        Assert.False(host.Online);
        Assert.Equal(0, host.FreeVcpu);
        Assert.Equal(0, host.FreeMemoryMiB);
    }

    [Fact]
    public void Free_disk_is_free_space_less_the_reserve_on_datastores_that_hold_disks()
    {
        var room = Read("proxmox", Snapshot(datastores:
        [
            Entry("local-lvm", ("content", "images,rootdir"), ("freeSpace", 100 * GiB)),
            Entry("iso", ("content", "iso,vztmpl"), ("freeSpace", 500 * GiB)),
            Entry("unsized", ("content", "images")),
        ]));

        var datastore = Assert.Single(room.Datastores);
        Assert.Equal("local-lvm", datastore.Name);
        Assert.Equal(90, datastore.FreeGiB);
        Assert.Contains("proxmox", room.DatastoresRead);
    }

    [Fact]
    public void An_inaccessible_vsphere_datastore_is_no_room()
    {
        var room = Read("vsphere", Snapshot(datastores:
        [
            Entry("datastore-1", ("accessible", "true"), ("freeSpace", 10 * GiB)),
            Entry("datastore-2", ("accessible", "false"), ("freeSpace", 10 * GiB)),
        ]));

        Assert.Equal(["datastore-1"], room.Datastores.Select(x => x.Id));
    }

    [Fact]
    public void Hosts_that_could_not_be_read_are_not_read()
    {
        var room = Read("vsphere", Snapshot());

        Assert.Empty(room.HostsRead);
        Assert.Empty(room.DatastoresRead);
    }

    [Fact]
    public void A_stale_read_is_noted()
    {
        var snapshot = Snapshot([Entry("pve1", ("maxcpu", 4))]);
        snapshot.Hosts.Stale = true;

        Assert.Contains(Read("proxmox", snapshot).Notes, x => x.Contains("earlier read"));
    }
}
