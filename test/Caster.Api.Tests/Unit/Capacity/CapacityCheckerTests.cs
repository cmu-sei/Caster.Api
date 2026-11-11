// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Caster.Api.Domain.Services.Capacity;
using Caster.Api.Infrastructure.Options;

namespace Caster.Api.Tests.Unit.Capacity;

[Trait("Category", "Unit")]
public class CapacityCheckerTests
{
    private static MachineSize Size(int vcpu, long memoryGiB, params (string Datastore, double Gib)[] disks) => new()
    {
        Vcpu = vcpu,
        MemoryMiB = memoryGiB * 1024,
        Disks = [.. disks.Select(x => new PlannedDisk { Datastore = x.Datastore, SizeGiB = x.Gib })],
    };

    private static PlannedMachine Create(string address, MachineSize size, string host = null, string hypervisor = "proxmox") =>
        new() { Address = address, Hypervisor = hypervisor, Action = "create", Host = host, After = size };

    private static PlannedMachine Keep(string address, MachineSize size, string host = null) =>
        new() { Address = address, Hypervisor = "proxmox", Action = "no-op", Host = host, Before = size, After = size };

    private static DeploymentDemand Demand(params PlannedMachine[] machines) => new() { Machines = [.. machines] };

    private static HostRoom Host(string name, int freeVcpu, long freeMemoryGiB, bool online = true) => new()
    {
        Connection = "lab",
        Hypervisor = "proxmox",
        Id = name,
        Name = name,
        Online = online,
        FreeVcpu = online ? freeVcpu : 0,
        FreeMemoryMiB = online ? freeMemoryGiB * 1024 : 0,
    };

    private static HypervisorRoom Room(HostRoom[] hosts, params (string Name, double FreeGiB)[] datastores)
    {
        var room = new HypervisorRoom
        {
            Hosts = [.. hosts],
            Datastores = [.. datastores.Select(x => new DatastoreRoom { Connection = "lab", Hypervisor = "proxmox", Id = x.Name, Name = x.Name, FreeGiB = x.FreeGiB })],
        };
        room.HostsRead.Add("proxmox");

        if (datastores.Length > 0)
            room.DatastoresRead.Add("proxmox");

        return room;
    }

    private static EffectiveLimits NoLimits => new();

    private static CapacityCheck Check(DeploymentDemand demand, HypervisorRoom room, EffectiveLimits limits = null, RoomCheckMode mode = RoomCheckMode.Warn) =>
        CapacityChecker.Check(demand, limits ?? NoLimits, room, new CapacityOptions { RoomCheck = mode });

    #region Room

    [Fact]
    public void A_machine_placed_on_a_host_is_counted_against_that_host_only()
    {
        var check = Check(
            Demand(Create("a", Size(8, 16), "pve1"), Create("b", Size(2, 2), "pve2")),
            Room([Host("pve1", 10, 32), Host("pve2", 1, 64)]));

        Assert.Equal(CapacityCheckStatus.Warning, check.Status);
        var pve1 = Assert.Single(check.Hosts, x => x.Host == "pve1");
        Assert.True(pve1.Fits);
        Assert.Equal(["a"], pve1.Machines);
        var pve2 = Assert.Single(check.Hosts, x => x.Host == "pve2");
        Assert.False(pve2.Fits);
        Assert.Equal(2, pve2.NeedVcpu);
        Assert.Equal("pve2 needs 2 vCPUs and 2 GiB of memory and has 1 vCPU and 64 GiB of memory free.", pve2.Message);
    }

    [Fact]
    public void Machines_placed_on_no_host_share_what_the_placed_ones_leave()
    {
        // pve1 has 10 free, and a placed machine takes 8 of them: 2 + 4 are left for the rest.
        var check = Check(
            Demand(Create("placed", Size(8, 8), "pve1"), Create("free1", Size(4, 4)), Create("free2", Size(2, 2))),
            Room([Host("pve1", 10, 32), Host("pve2", 4, 32)]));

        var aggregate = Assert.Single(check.Hosts, x => x.Aggregate);
        Assert.Equal(["free1", "free2"], aggregate.Machines);
        Assert.Equal(6, aggregate.NeedVcpu);
        Assert.Equal(6, aggregate.FreeVcpu);
        Assert.True(aggregate.Fits);
        Assert.Equal(CapacityCheckStatus.Ok, check.Status);
    }

    [Fact]
    public void A_machine_on_a_host_the_inventory_does_not_list_is_counted_against_all_of_them()
    {
        var check = Check(Demand(Create("a", Size(2, 2), "pve9")), Room([Host("pve1", 10, 32)]));

        Assert.True(Assert.Single(check.Hosts).Aggregate);
        Assert.Contains(check.Notes, x => x.Contains("pve9, which the inventory does not list"));
    }

    [Fact]
    public void An_offline_host_does_not_fit_and_gives_no_room_to_the_rest()
    {
        var check = Check(
            Demand(Create("a", Size(1, 1), "pve2"), Create("b", Size(4, 4))),
            Room([Host("pve1", 2, 32), Host("pve2", 64, 256, online: false)]));

        Assert.False(Assert.Single(check.Hosts, x => x.Host == "pve2").Fits);
        Assert.Contains("offline", check.Hosts.Single(x => x.Host == "pve2").Message);
        var aggregate = Assert.Single(check.Hosts, x => x.Aggregate);
        Assert.Equal(2, aggregate.FreeVcpu);
        Assert.False(aggregate.Fits);
    }

    [Fact]
    public void Disks_are_counted_against_their_datastores()
    {
        var check = Check(
            Demand(Create("a", Size(1, 1, ("local-lvm", 40), ("ceph", 500)), "pve1"), Create("b", Size(1, 1, ("nfs", 10)))),
            Room([Host("pve1", 10, 32)], ("local-lvm", 50), ("ceph", 100)));

        Assert.True(check.Datastores.Single(x => x.Datastore == "local-lvm").Fits);
        Assert.False(check.Datastores.Single(x => x.Datastore == "ceph").Fits);
        // nfs isn't listed: it goes against what local-lvm and ceph have left between them.
        var aggregate = Assert.Single(check.Datastores, x => x.Aggregate);
        Assert.Equal(10, aggregate.NeedGiB);
        Assert.Equal(10, aggregate.FreeGiB);
        Assert.Equal(CapacityCheckStatus.Warning, check.Status);
    }

    [Fact]
    public void Only_what_a_run_adds_needs_room()
    {
        var grown = new PlannedMachine
        {
            Address = "grown",
            Hypervisor = "proxmox",
            Action = "update",
            Host = "pve1",
            Before = Size(2, 8, ("local-lvm", 40)),
            After = Size(4, 4, ("local-lvm", 50)),
        };

        var check = Check(
            Demand(Keep("kept", Size(32, 128), "pve1"), grown),
            Room([Host("pve1", 2, 1)], ("local-lvm", 10)));

        var pve1 = Assert.Single(check.Hosts);
        Assert.Equal(2, pve1.NeedVcpu);
        Assert.Equal(0, pve1.NeedMemoryMiB);
        Assert.Equal(10, Assert.Single(check.Datastores).NeedGiB);
        Assert.Equal(CapacityCheckStatus.Ok, check.Status);
        Assert.Equal(0, check.Requested.Machines);
        Assert.Equal(2, check.Requested.Vcpu);
    }

    [Fact]
    public void Room_on_a_hypervisor_without_inventory_is_not_checked()
    {
        var check = Check(Demand(Create("a", Size(64, 512), hypervisor: "vsphere")), Room([Host("pve1", 1, 1)]));

        Assert.Empty(check.Hosts);
        Assert.Equal(CapacityCheckStatus.Ok, check.Status);
        Assert.Contains(check.Notes, x => x.Contains("vSphere hosts was not checked"));
    }

    [Fact]
    public void Out_of_room_is_a_warning_to_acknowledge_in_warn_mode()
    {
        var check = Check(Demand(Create("a", Size(8, 8), "pve1")), Room([Host("pve1", 1, 1)]));

        Assert.Equal(CapacityCheckStatus.Warning, check.Status);
        Assert.True(check.CanApply);
        Assert.True(check.RequiresAcknowledgement);
        Assert.StartsWith("The hypervisors may not have room for this run.", check.Message);
    }

    [Fact]
    public void Out_of_room_is_refused_in_block_mode()
    {
        var check = Check(Demand(Create("a", Size(8, 8), "pve1")), Room([Host("pve1", 1, 1)]), mode: RoomCheckMode.Block);

        Assert.Equal(CapacityCheckStatus.Refused, check.Status);
        Assert.False(check.CanApply);
        Assert.False(check.RequiresAcknowledgement);
    }

    #endregion

    #region Limits

    [Fact]
    public void Limits_are_on_what_the_workspace_has_after_the_run()
    {
        var limits = new EffectiveLimits { MaxMachines = new() { Value = 2, Source = LimitSource.Project } };

        var check = Check(Demand(Keep("a", Size(1, 1)), Keep("b", Size(1, 1)), Create("c", Size(1, 1))), new HypervisorRoom(), limits);

        Assert.Equal(CapacityCheckStatus.Refused, check.Status);
        Assert.False(check.CanApply);
        var violation = Assert.Single(check.LimitViolations);
        Assert.Equal("MaxMachines", violation.Limit);
        Assert.Equal(3, violation.Total);
        Assert.Equal(2, violation.Before);
        Assert.Equal("This run would leave the workspace with 3 machines, over its limit of 2 machines (set on the project); it has 2 now.", violation.Message);
    }

    [Fact]
    public void Every_limit_is_checked_in_its_own_unit()
    {
        var limits = new EffectiveLimits
        {
            MaxVcpu = new() { Value = 4, Source = LimitSource.Global },
            MaxMemoryGiB = new() { Value = 8, Source = LimitSource.Workspace },
            MaxDiskGiB = new() { Value = 100, Source = LimitSource.Global },
        };

        var check = Check(Demand(Create("a", Size(6, 12, ("ceph", 120)))), new HypervisorRoom(), limits);

        Assert.Equal(["MaxVcpu", "MaxMemoryGiB", "MaxDiskGiB"], check.LimitViolations.Select(x => x.Limit));
        Assert.Equal(12, check.LimitViolations[1].Total);
    }

    [Fact]
    public void A_run_that_does_not_grow_a_workspace_over_a_limit_is_not_refused()
    {
        var limits = new EffectiveLimits { MaxVcpu = new() { Value = 4, Source = LimitSource.Project } };
        var shrinking = new PlannedMachine { Address = "s", Hypervisor = "proxmox", Action = "update", Before = Size(16, 8), After = Size(8, 8) };

        var check = Check(Demand(shrinking, new PlannedMachine { Address = "gone", Action = "delete", Hypervisor = "proxmox", Before = Size(4, 4) }), new HypervisorRoom(), limits);

        Assert.Empty(check.LimitViolations);
        Assert.Equal(CapacityCheckStatus.Ok, check.Status);
    }

    [Fact]
    public void A_limit_wins_over_a_room_warning()
    {
        var limits = new EffectiveLimits { MaxMachines = new() { Value = 0, Source = LimitSource.Workspace } };

        var check = Check(Demand(Create("a", Size(8, 8), "pve1")), Room([Host("pve1", 1, 1)]), limits);

        Assert.Equal(CapacityCheckStatus.Refused, check.Status);
        Assert.False(check.RequiresAcknowledgement);
        Assert.Single(check.Hosts, x => !x.Fits);
    }

    #endregion

    [Fact]
    public void Totals_add_up_before_and_after()
    {
        var check = Check(
            Demand(Keep("a", Size(2, 4, ("x", 10))), Create("b", Size(4, 8, ("x", 20), ("y", 5)))),
            new HypervisorRoom());

        Assert.Equal((1, 2, 4096L, 10d), (check.Before.Machines, check.Before.Vcpu, check.Before.MemoryMiB, check.Before.DiskGiB));
        Assert.Equal((2, 6, 12288L, 35d), (check.After.Machines, check.After.Vcpu, check.After.MemoryMiB, check.After.DiskGiB));
        Assert.Equal((1, 4, 8192L, 25d), (check.Requested.Machines, check.Requested.Vcpu, check.Requested.MemoryMiB, check.Requested.DiskGiB));
    }

    [Fact]
    public void A_plan_whose_machines_are_unknown_is_not_checked()
    {
        var check = CapacityChecker.Check(null, new EffectiveLimits { MaxMachines = new() { Value = 0 } }, null, new CapacityOptions());

        Assert.Equal(CapacityCheckStatus.Unknown, check.Status);
        Assert.True(check.CanApply);
    }

    [Fact]
    public void The_fixture_plan_totals()
    {
        var demand = PlanCapacityParser.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(System.AppContext.BaseDirectory, "Data", "capacity", "providers.plan.json")));

        var check = CapacityChecker.Check(demand, NoLimits, new HypervisorRoom(), new CapacityOptions());

        Assert.Equal(8, check.Requested.Machines);
        Assert.Equal(25, check.Requested.Vcpu);
        Assert.Equal(24064, check.Requested.MemoryMiB);
        Assert.Equal(1376.5, check.Requested.DiskGiB);
        Assert.Equal(11, check.After.Machines);
        Assert.Equal(4, check.Before.Machines);
    }
}
