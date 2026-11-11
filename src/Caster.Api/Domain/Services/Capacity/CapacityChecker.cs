// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Caster.Api.Infrastructure.Options;

namespace Caster.Api.Domain.Services.Capacity;

/// <summary>
/// Checks what a plan leaves a workspace with against its effective limits,
/// and what it adds against the room on the hypervisors.
/// </summary>
/// <remarks>
/// <para>
/// Limits are on the workspace as a whole after the run: its machines that
/// stay, those it updates, and those it creates. A run is only over a limit
/// that it takes the workspace further over, so a run that shrinks a workspace
/// already over a lowered limit, or destroys it, is never refused.
/// </para>
/// <para>
/// Room is for what the run adds: every machine it creates, and what it gives
/// a machine it updates or replaces beyond what that had. What a run frees is
/// not counted. A machine the plan places on a host (a Proxmox
/// <c>node_name</c>/<c>target_node</c>, a vSphere <c>host_system_id</c>) is
/// counted against that host. One it places on none, or on a host the
/// inventory does not list, is counted against every online host of its
/// provider between them, less what the placed machines take. Disks go the
/// same way by datastore.
/// </para>
/// </remarks>
public static class CapacityChecker
{
    public static CapacityCheck Check(DeploymentDemand demand, EffectiveLimits limits, HypervisorRoom room, CapacityOptions options)
    {
        var check = new CapacityCheck
        {
            RoomCheck = options.RoomCheck,
            CpuOvercommitRatio = options.CpuOvercommitRatio,
            ReservePercent = options.ReservePercent,
            Limits = limits ?? new EffectiveLimits(),
            InventoryReadAt = room?.ReadAt,
        };

        if (demand == null)
        {
            check.Status = CapacityCheckStatus.Unknown;
            check.Message = "The machines in this plan could not be read, so its size was not checked.";
            return check;
        }

        check.Notes.AddRange(demand.Notes);

        var machines = demand.Machines ?? [];
        check.Before = Totals(machines.Where(x => x.Before != null).Select(x => x.Before));
        check.After = Totals(machines.Where(x => x.After != null).Select(x => x.After));

        var growth = machines.Select(x => (Machine: x, Growth: Growth(x))).Where(x => x.Growth.Any).ToList();
        check.Requested = new CapacityTotals
        {
            Machines = machines.Count(x => x.Before == null && x.After != null),
            Vcpu = growth.Sum(x => x.Growth.Vcpu),
            MemoryMiB = growth.Sum(x => x.Growth.MemoryMiB),
            DiskGiB = Round(growth.Sum(x => x.Growth.Disks.Sum(d => d.SizeGiB))),
            Incomplete = growth.Count(x => x.Machine.After.Incomplete),
        };

        CheckLimits(check);
        CheckRoom(check, growth, room ?? new HypervisorRoom());

        room?.Notes.ForEach(check.Notes.Add);

        if (check.LimitViolations.Count > 0)
        {
            check.Status = CapacityCheckStatus.Refused;
            check.CanApply = false;
            check.Message = string.Join(" ", check.LimitViolations.Select(x => x.Message));
        }
        else if (check.Hosts.Any(x => !x.Fits) || check.Datastores.Any(x => !x.Fits))
        {
            var lines = check.Hosts.Where(x => !x.Fits).Select(x => x.Message)
                .Concat(check.Datastores.Where(x => !x.Fits).Select(x => x.Message));
            var text = $"The hypervisors may not have room for this run. {string.Join(" ", lines)}";

            if (options.RoomCheck == RoomCheckMode.Block)
            {
                check.Status = CapacityCheckStatus.Refused;
                check.CanApply = false;
                check.Message = text;
            }
            else
            {
                check.Status = CapacityCheckStatus.Warning;
                check.RequiresAcknowledgement = true;
                check.Message = text;
            }
        }
        else
        {
            check.Status = CapacityCheckStatus.Ok;
        }

        return check;
    }

    /// <summary>What one machine adds: all of it when created, what it gains when updated or replaced.</summary>
    private static Increase Growth(PlannedMachine machine)
    {
        if (machine.After == null)
            return new Increase();

        if (machine.Before == null)
            return new Increase { Vcpu = machine.After.Vcpu, MemoryMiB = machine.After.MemoryMiB, Disks = [.. machine.After.Disks] };

        var disks = new List<PlannedDisk>();

        foreach (var group in machine.After.Disks.GroupBy(x => x.Datastore))
        {
            var before = machine.Before.Disks.Where(x => x.Datastore == group.Key).Sum(x => x.SizeGiB);
            var more = group.Sum(x => x.SizeGiB) - before;

            if (more > 0)
                disks.Add(new PlannedDisk { Datastore = group.Key, SizeGiB = more });
        }

        return new Increase
        {
            Vcpu = Math.Max(0, machine.After.Vcpu - machine.Before.Vcpu),
            MemoryMiB = Math.Max(0, machine.After.MemoryMiB - machine.Before.MemoryMiB),
            Disks = disks,
        };
    }

    private sealed class Increase
    {
        public int Vcpu { get; init; }
        public long MemoryMiB { get; init; }
        public List<PlannedDisk> Disks { get; init; } = [];
        public bool Any => Vcpu > 0 || MemoryMiB > 0 || Disks.Count > 0;
    }

    private static CapacityTotals Totals(IEnumerable<MachineSize> sizes)
    {
        var list = sizes.ToList();

        return new CapacityTotals
        {
            Machines = list.Count,
            Vcpu = list.Sum(x => x.Vcpu),
            MemoryMiB = list.Sum(x => x.MemoryMiB),
            DiskGiB = Round(list.Sum(x => x.Disks.Sum(d => d.SizeGiB))),
            Incomplete = list.Count(x => x.Incomplete),
        };
    }

    #region Limits

    private static void CheckLimits(CapacityCheck check)
    {
        Limit(check, nameof(EffectiveLimits.MaxMachines), check.Limits.MaxMachines, check.After.Machines, check.Before.Machines, "machine", "machines");
        Limit(check, nameof(EffectiveLimits.MaxVcpu), check.Limits.MaxVcpu, check.After.Vcpu, check.Before.Vcpu, "vCPU", "vCPUs");
        Limit(check, nameof(EffectiveLimits.MaxMemoryGiB), check.Limits.MaxMemoryGiB, Round(check.After.MemoryMiB / 1024d), Round(check.Before.MemoryMiB / 1024d), "GiB of memory", "GiB of memory");
        Limit(check, nameof(EffectiveLimits.MaxDiskGiB), check.Limits.MaxDiskGiB, check.After.DiskGiB, check.Before.DiskGiB, "GiB of disk", "GiB of disk");
    }

    private static void Limit(CapacityCheck check, string name, EffectiveLimit limit, double total, double before, string one, string many)
    {
        if (limit == null || total <= limit.Value || total <= before)
            return;

        check.LimitViolations.Add(new CapacityLimitViolation
        {
            Limit = name,
            Value = limit.Value,
            Source = limit.Source,
            Total = total,
            Before = before,
            Message = $"This run would leave the workspace with {Amount(total, one, many)}, over its limit of {Amount(limit.Value, one, many)} ({Where(limit.Source)}); it has {Number(before)} now.",
        });
    }

    private static string Where(LimitSource source) => source switch
    {
        LimitSource.Workspace => "set on the workspace",
        LimitSource.Project => "set on the project",
        _ => "set in the Capacity settings",
    };

    #endregion

    #region Room

    private static void CheckRoom(CapacityCheck check, List<(PlannedMachine Machine, Increase Growth)> growth, HypervisorRoom room)
    {
        foreach (var byHypervisor in growth.Where(x => x.Machine.Hypervisor != null).GroupBy(x => x.Machine.Hypervisor, StringComparer.OrdinalIgnoreCase))
        {
            var hypervisor = byHypervisor.Key;
            var label = Label(hypervisor);
            var items = byHypervisor.ToList();

            if (items.Any(x => x.Growth.Vcpu > 0 || x.Growth.MemoryMiB > 0))
            {
                if (room.HostsRead.Contains(hypervisor))
                    CheckHosts(check, hypervisor, items, room.Hosts.Where(x => string.Equals(x.Hypervisor, hypervisor, StringComparison.OrdinalIgnoreCase)).ToList());
                else
                    check.Notes.Add($"Room on the {label} hosts was not checked: the inventory has none to read.");
            }

            if (items.Any(x => x.Growth.Disks.Count > 0))
            {
                if (room.DatastoresRead.Contains(hypervisor))
                    CheckDatastores(check, hypervisor, items, room.Datastores.Where(x => string.Equals(x.Hypervisor, hypervisor, StringComparison.OrdinalIgnoreCase)).ToList());
                else
                    check.Notes.Add($"Room on the {label} datastores was not checked: the inventory has none with their free space.");
            }
        }
    }

    private static void CheckHosts(CapacityCheck check, string hypervisor, List<(PlannedMachine Machine, Increase Growth)> items, List<HostRoom> hosts)
    {
        var placed = new Dictionary<HostRoom, CapacityHostLine>();
        var unplaced = new CapacityHostLine { Hypervisor = hypervisor };

        foreach (var (machine, growth) in items.Where(x => x.Growth.Vcpu > 0 || x.Growth.MemoryMiB > 0))
        {
            var host = FindHost(check, hypervisor, machine, hosts);
            CapacityHostLine line;

            if (host == null)
            {
                line = unplaced;
            }
            else if (!placed.TryGetValue(host, out line))
            {
                line = new CapacityHostLine
                {
                    Hypervisor = hypervisor,
                    Connection = host.Connection,
                    Host = host.Name,
                    FreeVcpu = host.FreeVcpu,
                    FreeMemoryMiB = host.FreeMemoryMiB,
                    CpuApproximated = host.CpuApproximated,
                    Online = host.Online,
                };
                placed[host] = line;
            }

            line.Machines.Add(machine.Address);
            line.NeedVcpu += growth.Vcpu;
            line.NeedMemoryMiB += growth.MemoryMiB;
        }

        foreach (var (host, line) in placed)
        {
            line.Fits = line.Online && line.NeedVcpu <= line.FreeVcpu && line.NeedMemoryMiB <= line.FreeMemoryMiB;
            line.Message = line.Online
                ? $"{line.Host} needs {Needs(line.NeedVcpu, line.NeedMemoryMiB)} and has {Needs(line.FreeVcpu, line.FreeMemoryMiB)} free."
                : $"{line.Host} is offline, and the plan puts {Amount(line.Machines.Count, "machine", "machines")} on it.";
            check.Hosts.Add(line);
        }

        if (unplaced.Machines.Count > 0)
        {
            var online = hosts.Where(x => x.Online).ToList();
            unplaced.FreeVcpu = online.Sum(x => Math.Max(0, x.FreeVcpu - (placed.TryGetValue(x, out var l) ? l.NeedVcpu : 0)));
            unplaced.FreeMemoryMiB = online.Sum(x => Math.Max(0, x.FreeMemoryMiB - (placed.TryGetValue(x, out var l) ? l.NeedMemoryMiB : 0)));
            unplaced.CpuApproximated = online.Any(x => x.CpuApproximated);
            unplaced.Fits = unplaced.NeedVcpu <= unplaced.FreeVcpu && unplaced.NeedMemoryMiB <= unplaced.FreeMemoryMiB;
            unplaced.Message = $"The {Label(hypervisor)} hosts between them need {Needs(unplaced.NeedVcpu, unplaced.NeedMemoryMiB)} for machines the plan places on no host, and have {Needs(unplaced.FreeVcpu, unplaced.FreeMemoryMiB)} free.";
            check.Hosts.Add(unplaced);
        }

        if (check.Hosts.Any(x => x.CpuApproximated))
            check.Notes.Add("Some hosts do not say what their VMs are given, so their free vCPU is from their current CPU load.");
    }

    private static HostRoom FindHost(CapacityCheck check, string hypervisor, PlannedMachine machine, List<HostRoom> hosts)
    {
        if (string.IsNullOrEmpty(machine.Host))
            return null;

        var matches = hosts
            .Where(x => string.Equals(x.Id, machine.Host, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Name, machine.Host, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
        {
            check.Notes.Add($"{machine.Address} goes on {machine.Host}, which the inventory does not list, so it is counted against every {Label(hypervisor)} host.");
            return null;
        }

        if (matches.Select(x => x.Connection).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            check.Notes.Add($"{machine.Host} is a host of more than one connection; {machine.Address} is counted on the one in {matches[0].Connection}.");

        return matches[0];
    }

    private static void CheckDatastores(CapacityCheck check, string hypervisor, List<(PlannedMachine Machine, Increase Growth)> items, List<DatastoreRoom> datastores)
    {
        var placed = new Dictionary<DatastoreRoom, CapacityDatastoreLine>();
        var unplaced = new CapacityDatastoreLine { Hypervisor = hypervisor };
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var disk in items.SelectMany(x => x.Growth.Disks))
        {
            var datastore = string.IsNullOrEmpty(disk.Datastore)
                ? null
                : datastores.FirstOrDefault(x => string.Equals(x.Id, disk.Datastore, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Name, disk.Datastore, StringComparison.OrdinalIgnoreCase));

            if (datastore == null)
            {
                if (!string.IsNullOrEmpty(disk.Datastore))
                    missing.Add(disk.Datastore);

                unplaced.NeedGiB += disk.SizeGiB;
                continue;
            }

            if (!placed.TryGetValue(datastore, out var line))
            {
                line = new CapacityDatastoreLine { Hypervisor = hypervisor, Connection = datastore.Connection, Datastore = datastore.Name, FreeGiB = datastore.FreeGiB };
                placed[datastore] = line;
            }

            line.NeedGiB += disk.SizeGiB;
        }

        foreach (var name in missing)
            check.Notes.Add($"The plan puts disks on {name}, which the inventory does not list with its free space, so they are counted against every {Label(hypervisor)} datastore.");

        foreach (var line in placed.Values)
        {
            line.NeedGiB = Round(line.NeedGiB);
            line.Fits = line.NeedGiB <= line.FreeGiB;
            line.Message = $"{line.Datastore} needs {Number(line.NeedGiB)} GiB and has {Number(line.FreeGiB)} GiB free.";
            check.Datastores.Add(line);
        }

        if (unplaced.NeedGiB > 0)
        {
            unplaced.NeedGiB = Round(unplaced.NeedGiB);
            unplaced.FreeGiB = Round(datastores.Sum(x => Math.Max(0, x.FreeGiB - (placed.TryGetValue(x, out var l) ? l.NeedGiB : 0))));
            unplaced.Fits = unplaced.NeedGiB <= unplaced.FreeGiB;
            unplaced.Message = $"The {Label(hypervisor)} datastores between them need {Number(unplaced.NeedGiB)} GiB for disks the plan places on none, and have {Number(unplaced.FreeGiB)} GiB free.";
            check.Datastores.Add(unplaced);
        }
    }

    #endregion

    #region Text

    private static string Label(string hypervisor) => hypervisor?.ToLowerInvariant() switch
    {
        InfrastructureProviders.Proxmox => "Proxmox",
        InfrastructureProviders.Vsphere => "vSphere",
        _ => hypervisor,
    };

    private static string Needs(int vcpu, long memoryMiB) =>
        $"{Amount(vcpu, "vCPU", "vCPUs")} and {Number(Round(memoryMiB / 1024d))} GiB of memory";

    private static string Amount(double value, string one, string many) =>
        $"{Number(value)} {(value == 1 ? one : many)}";

    private static string Number(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private static double Round(double value) => Math.Round(value, 1);

    #endregion
}
