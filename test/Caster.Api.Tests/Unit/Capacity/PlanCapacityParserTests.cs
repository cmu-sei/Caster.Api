// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.IO;
using System.Linq;
using Caster.Api.Domain.Services.Capacity;
using File = System.IO.File;

namespace Caster.Api.Tests.Unit.Capacity;

/// <summary>
/// Reads Data/capacity/providers.plan.json: one machine of each resource type
/// Caster sizes (bpg, Telmate, vSphere), an update, a delete, a no-op with a
/// deposed object, a cloud machine, a Player record, and a machine only in the
/// prior state.
/// </summary>
[Trait("Category", "Unit")]
public class PlanCapacityParserTests
{
    private static readonly DeploymentDemand Demand = PlanCapacityParser.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "capacity", "providers.plan.json")));

    private static PlannedMachine Machine(string address) => Assert.Single(Demand.Machines, x => x.Address == address);

    private static (string, double)[] Disks(MachineSize size) => [.. size.Disks.Select(x => (x.Datastore, x.SizeGiB))];

    [Fact]
    public void Counts_every_machine_once_and_nothing_else()
    {
        Assert.Equal(12, Demand.Machines.Count);
        Assert.DoesNotContain(Demand.Machines, x => x.Type == "crucible_player_virtual_machine");
        Assert.DoesNotContain(Demand.Machines, x => x.Type == "vsphere_datastore");
        Assert.Single(Demand.Machines, x => x.Address == "vsphere_virtual_machine.kept");
    }

    [Fact]
    public void Bpg_vm_is_cores_times_sockets_with_each_disk_on_its_datastore()
    {
        var web = Machine("proxmox_virtual_environment_vm.web");

        Assert.Equal("proxmox", web.Hypervisor);
        Assert.Equal("create", web.Action);
        Assert.Equal("pve1", web.Host);
        Assert.Null(web.Before);
        Assert.Equal(4, web.After.Vcpu);
        Assert.Equal(4096, web.After.MemoryMiB);
        Assert.Equal([("local-lvm", 32d), ("ceph", 100d)], Disks(web.After));
        // A disk path known only after apply is not a size.
        Assert.False(web.After.Incomplete);
    }

    [Fact]
    public void Bpg_clone_without_disks_is_incomplete_and_keeps_its_cpu_and_memory()
    {
        var dc = Machine("module.lab[\"dc\"].proxmox_virtual_environment_vm.vm");

        Assert.Equal("pve2", dc.Host);
        Assert.Equal(4, dc.After.Vcpu);
        Assert.Equal(8192, dc.After.MemoryMiB);
        Assert.Empty(dc.After.Disks);
        Assert.True(dc.After.Incomplete);
    }

    [Fact]
    public void Bpg_container_counts_its_root_disk_and_sized_mount_points_only()
    {
        var dns = Machine("proxmox_virtual_environment_container.dns");

        Assert.Equal(1, dns.After.Vcpu);
        Assert.Equal(1024, dns.After.MemoryMiB);
        Assert.Equal([("local-lvm", 8d), ("local-lvm", 10d)], Disks(dns.After));
    }

    [Fact]
    public void Telmate_2_reads_the_disk_list_and_skips_the_cloudinit_drive()
    {
        var legacy = Machine("proxmox_vm_qemu.legacy");

        Assert.Equal("pve1", legacy.Host);
        Assert.Equal(2, legacy.After.Vcpu);
        Assert.Equal(2048, legacy.After.MemoryMiB);
        Assert.Equal([("local-lvm", 20d)], Disks(legacy.After));
    }

    [Fact]
    public void Telmate_3_reads_the_cpu_block_and_the_disks_tree()
    {
        var modern = Machine("proxmox_vm_qemu.modern");

        Assert.Equal(8, modern.After.Vcpu);
        Assert.Equal(4096, modern.After.MemoryMiB);
        Assert.Equal([("ceph", 64d), ("ceph", 1024d)], Disks(modern.After));
        Assert.False(modern.After.Incomplete);
    }

    [Fact]
    public void Telmate_lxc_reads_rootfs_and_mountpoints()
    {
        var cache = Machine("proxmox_lxc.cache");

        Assert.Equal(2, cache.After.Vcpu);
        Assert.Equal(512, cache.After.MemoryMiB);
        Assert.Equal([("local-lvm", 8d), ("local-lvm", 0.5d)], Disks(cache.After));
    }

    [Fact]
    public void Vsphere_puts_a_disk_without_a_datastore_on_the_machines()
    {
        var ws = Machine("vsphere_virtual_machine.ws");

        Assert.Equal("vsphere", ws.Hypervisor);
        Assert.Equal("host-12", ws.Host);
        Assert.Equal(2, ws.After.Vcpu);
        Assert.Equal(4096, ws.After.MemoryMiB);
        Assert.Equal([("datastore-1", 60d), ("datastore-2", 40d)], Disks(ws.After));
    }

    [Fact]
    public void An_update_has_both_sizes_and_a_delete_only_the_old_one()
    {
        var app = Machine("vsphere_virtual_machine.app");
        Assert.Equal("update", app.Action);
        Assert.Equal(2, app.Before.Vcpu);
        Assert.Equal(4, app.After.Vcpu);
        Assert.Equal(2048, app.After.MemoryMiB);

        var old = Machine("vsphere_virtual_machine.old");
        Assert.Equal("delete", old.Action);
        Assert.Null(old.After);
        Assert.Equal(8, old.Before.Vcpu);
    }

    [Fact]
    public void A_cloud_machine_counts_as_a_machine_with_no_hypervisor()
    {
        var bastion = Machine("aws_instance.bastion");

        Assert.Null(bastion.Hypervisor);
        Assert.True(bastion.After.Incomplete);
        Assert.Contains(Demand.Notes, x => x.StartsWith("1 cloud machine"));
    }

    [Fact]
    public void A_machine_only_in_the_prior_state_is_kept_as_it_is()
    {
        var vm = Machine("module.untargeted.proxmox_virtual_environment_vm.vm");

        Assert.Equal("no-op", vm.Action);
        Assert.Equal(2, vm.After.Vcpu);
        Assert.Equal(vm.Before.MemoryMiB, vm.After.MemoryMiB);
    }

    [Fact]
    public void Says_which_machines_are_not_wholly_sized()
    {
        Assert.Contains(Demand.Notes, x => x.StartsWith("1 machine not wholly sized"));
    }

    [Fact]
    public void A_size_known_only_after_apply_makes_the_machine_incomplete()
    {
        var demand = PlanCapacityParser.Parse("""
            { "resource_changes": [ {
                "address": "proxmox_virtual_environment_vm.x", "mode": "managed", "type": "proxmox_virtual_environment_vm", "name": "x",
                "change": { "actions": ["create"], "before": null,
                  "after": { "node_name": "pve1", "cpu": [{ "cores": 2 }], "memory": [{}], "disk": [{ "datastore_id": "local-lvm", "size": 8 }] },
                  "after_unknown": { "memory": [{ "dedicated": true }] } } } ] }
            """);

        Assert.True(Assert.Single(demand.Machines).After.Incomplete);
    }

    [Theory]
    [InlineData("32G", 32)]
    [InlineData("32GB", 32)]
    [InlineData("512M", 0.5)]
    [InlineData("1T", 1024)]
    [InlineData("40", 40)]
    [InlineData("2.5G", 2.5)]
    public void Reads_size_strings_as_GiB(string text, double gib) => Assert.Equal(gib, PlanCapacityParser.Gib(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("big")]
    public void An_unreadable_size_is_none(string text) => Assert.Null(PlanCapacityParser.Gib(text));
}
