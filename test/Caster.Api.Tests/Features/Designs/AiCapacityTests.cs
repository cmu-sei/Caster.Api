// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.Capacity;
using Caster.Api.Domain.Services.Inventory;
using Caster.Api.Features.Designs;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Options;

namespace Caster.Api.Tests.Features.Designs;

/// <summary>
/// AI drafts and the deployment capacity check: the model is shown each host's room and the
/// limits, the topology plan is checked as a run would be, and a draft over a limit is not saved.
/// </summary>
[Trait("Category", "Unit")]
public class AiCapacityTests
{
    private const long GiB = 1024L * 1024 * 1024;

    /// <summary>The inventory with the room its own snapshots give, and the limits, as a draft handler adds them.</summary>
    public static async Task<AiInventory> WithCapacity(AiInventory inventory, EffectiveLimits limits = null, CapacityOptions options = null)
    {
        options ??= new CapacityOptions();
        var capacity = Substitute.For<ICapacityCheckService>();
        capacity.Options.Returns(options);
        capacity.GetDirectoryLimitsAsync(default, default).ReturnsForAnyArgs(limits ?? new EffectiveLimits());
        capacity.ReadRoom().Returns(HypervisorRoomReader.Read(
            inventory.Sources.Select(s => (s.Connection, AiInventory.ProviderOf(s.Hypervisor), s.Snapshot)), options));
        return await inventory.WithCapacity(capacity, Guid.NewGuid(), CancellationToken.None);
    }

    private static InventorySnapshot WithHosts(InventorySnapshot snapshot, params InventoryEntry[] hosts)
    {
        snapshot.Hosts = InventoryCategoryResult.Ok(hosts);
        return snapshot;
    }

    private static AiInventory Proxmox(params InventoryEntry[] hosts) =>
        AiInventory.Load(AiInventoryTests.Service(("lab", InfrastructureProviders.Proxmox, WithHosts(AiInventoryTests.Proxmox(), hosts))), null);

    private static InventoryEntry Node(string name, int cores, int allocated, long memoryGiB, long usedGiB, string status = "online") =>
        AiInventoryTests.Entry(name, ("status", status), ("maxcpu", cores.ToString()), ("allocatedCpus", allocated.ToString()),
            ("maxmem", (memoryGiB * GiB).ToString()), ("mem", (usedGiB * GiB).ToString()));

    private static AiTopologyPlan Plan(int machines, int cpu, int memoryMiB, int diskGiB) => new()
    {
        Machines = [.. Enumerable.Range(1, machines).Select(i => new AiTopologyMachine { Name = $"vm{i}", Cpu = cpu, MemoryMiB = memoryMiB, DiskGiB = diskGiB })],
    };

    [Fact]
    public async Task The_prompt_shows_each_hosts_room_and_the_limits()
    {
        var inventory = await WithCapacity(Proxmox(Node("pve1", 16, 40, 64, 20), Node("pve2", 8, 0, 32, 0, "offline")),
            new EffectiveLimits { MaxMachines = new() { Value = 12, Source = LimitSource.Project }, MaxVcpu = new() { Value = 48, Source = LimitSource.Global } });

        var prompt = inventory.BuildPromptSection();

        // 16 cores x 4 less 40 allocated; 64 GiB less 10% less 20 in use
        Assert.Contains("- pve1 (16 cores, 24 vCPU free, 37.6 GiB memory free)", prompt);
        Assert.Contains("- pve2 (offline: no room)", prompt);
        Assert.Contains("Deployment limits: each workspace may have at most 12 machines, 48 vCPU in all.", prompt);
    }

    [Fact]
    public async Task The_limits_are_shown_even_without_the_inventory()
    {
        var inventory = await WithCapacity(AiInventory.None("not checked"), new EffectiveLimits { MaxDiskGiB = new() { Value = 500, Source = LimitSource.Project } });

        Assert.Contains("at most 500 GiB of disk in all", inventory.BuildPromptSection());
        Assert.Null(inventory.Room);
    }

    [Fact]
    public void Without_limits_or_inventory_the_prompt_says_nothing()
    {
        Assert.Equal("", AiInventory.None("not checked").BuildPromptSection());
    }

    [Fact]
    public async Task The_plan_is_checked_against_every_hosts_room()
    {
        var inventory = await WithCapacity(Proxmox(Node("pve1", 4, 0, 16, 0), Node("pve2", 4, 0, 16, 0)));

        var warnings = AiTopology.Check(Plan(machines: 5, cpu: 8, memoryMiB: 2048, diskGiB: 10), inventory, Hypervisors.Proxmox);

        // 2 hosts x 4 cores x 4 = 32 vCPU free; 5 x 8 = 40 needed
        Assert.Contains(warnings, w => w.StartsWith("The hypervisors may not have room: The Proxmox hosts between them need 40 vCPUs"));
        var line = Assert.Single(inventory.Room.Hosts, h => h.Name == "pve1");
        Assert.Equal(16, line.FreeVcpu);
    }

    [Fact]
    public async Task A_plan_over_a_limit_is_warned_about()
    {
        var inventory = await WithCapacity(Proxmox(Node("pve1", 64, 0, 512, 0)),
            new EffectiveLimits { MaxMachines = new() { Value = 3, Source = LimitSource.Project } });

        var plan = Plan(machines: 4, cpu: 1, memoryMiB: 1024, diskGiB: 10);
        var warnings = AiTopology.Check(plan, inventory, Hypervisors.Proxmox);

        Assert.Contains("The machines need 4 machines, over the limit of 3 machines (set on the project) for a workspace. A draft over a limit cannot be saved.", warnings);
        Assert.Equal(CapacityCheckStatus.Refused, plan.Totals.Capacity.Status);
    }

    [Fact]
    public async Task A_plan_within_the_room_and_limits_has_no_capacity_warning()
    {
        var inventory = await WithCapacity(Proxmox(Node("pve1", 64, 0, 512, 0)),
            new EffectiveLimits { MaxMachines = new() { Value = 10, Source = LimitSource.Global } });

        var plan = Plan(machines: 2, cpu: 2, memoryMiB: 2048, diskGiB: 10);
        var warnings = AiTopology.Check(plan, inventory, Hypervisors.Proxmox);

        Assert.DoesNotContain(warnings, w => w.Contains("room") || w.Contains("limit"));
        Assert.Equal(CapacityCheckStatus.Ok, plan.Totals.Capacity.Status);
    }

    private static ICapacityCheckService Capacity(EffectiveLimits limits)
    {
        var capacity = Substitute.For<ICapacityCheckService>();
        capacity.Options.Returns(new CapacityOptions());
        capacity.GetDirectoryLimitsAsync(default, default).ReturnsForAnyArgs(limits);
        return capacity;
    }

    [Fact]
    public async Task Saving_a_draft_over_a_limit_is_refused()
    {
        var capacity = Capacity(new EffectiveLimits { MaxVcpu = new() { Value = 8, Source = LimitSource.Project } });

        var refused = await Assert.ThrowsAsync<ConflictException>(() => AiTopology.EnsureWithinLimits(
            Plan(machines: 3, cpu: 4, memoryMiB: 1024, diskGiB: 10), null, capacity, Guid.NewGuid(), "design", TestContext.Current.CancellationToken));

        Assert.Equal("This design cannot be saved. The machines need 12 vCPUs, over the limit of 8 vCPUs (set on the project) for a workspace. A draft over a limit cannot be saved.", refused.Message);
    }

    [Fact]
    public async Task Saving_takes_unsized_machines_from_their_template()
    {
        var capacity = Capacity(new EffectiveLimits { MaxMemoryGiB = new() { Value = 8, Source = LimitSource.Global } });
        var plan = new AiTopologyPlan { Machines = [new() { Name = "a", Template = "ubuntu-22" }, new() { Name = "b", Template = "ubuntu-22" }, new() { Name = "c", Template = "ubuntu-22" }] };

        // ubuntu-22 has 4 GiB, so three of them are 12 GiB
        await Assert.ThrowsAsync<ConflictException>(() => AiTopology.EnsureWithinLimits(
            plan, AiInventoryTests.Service(("lab", InfrastructureProviders.Proxmox, AiInventoryTests.Proxmox())), capacity, Guid.NewGuid(), "design", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Saving_within_the_limits_or_without_a_plan_or_limits_is_allowed()
    {
        var limited = Capacity(new EffectiveLimits { MaxMachines = new() { Value = 5, Source = LimitSource.Project } });
        var unlimited = Capacity(new EffectiveLimits());
        var ct = TestContext.Current.CancellationToken;

        await AiTopology.EnsureWithinLimits(Plan(5, 1, 1024, 10), null, limited, Guid.NewGuid(), "design", ct);
        await AiTopology.EnsureWithinLimits(null, null, limited, Guid.NewGuid(), "design", ct);
        await AiTopology.EnsureWithinLimits(Plan(500, 64, 65536, 1000), null, unlimited, Guid.NewGuid(), "design", ct);
        await AiTopology.EnsureWithinLimits(Plan(500, 64, 65536, 1000), null, null, Guid.NewGuid(), "design", ct);
    }
}
