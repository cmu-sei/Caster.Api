// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Caster.Api.Infrastructure.Options;

namespace Caster.Api.Domain.Services.Capacity;

/// <summary>
/// The machines a plan leaves a workspace with, and what each is given before
/// and after it is applied. Read from <c>terraform show -json</c> of the plan
/// when it is made, and saved on the plan, so the check before apply can be
/// made against the limits and the room as they are then.
/// </summary>
public class DeploymentDemand
{
    public List<PlannedMachine> Machines { get; set; } = [];

    /// <summary>What could not be read, in words, such as a disk whose size is only known after apply.</summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>One machine resource instance in a plan.</summary>
public class PlannedMachine
{
    /// <summary>The resource instance address, such as <c>module.vm["dc"].proxmox_virtual_environment_vm.vm</c>.</summary>
    public string Address { get; set; }

    public string Type { get; set; }

    /// <summary>
    /// The provider of the hypervisor it runs on, as in <see cref="InfrastructureProviders"/>:
    /// <c>proxmox</c> or <c>vsphere</c>. Null for a cloud machine, which takes no room on a hypervisor.
    /// </summary>
    public string Hypervisor { get; set; }

    /// <summary>create, update, replace, delete or no-op.</summary>
    public string Action { get; set; }

    /// <summary>
    /// Where the plan puts it: a Proxmox node name (<c>node_name</c>, <c>target_node</c>)
    /// or a vSphere host's managed object id (<c>host_system_id</c>). Null when the plan
    /// does not say, and the hypervisor picks.
    /// </summary>
    public string Host { get; set; }

    /// <summary>What it has now. Null when the plan creates it.</summary>
    public MachineSize Before { get; set; }

    /// <summary>What it will have. Null when the plan deletes it.</summary>
    public MachineSize After { get; set; }
}

/// <summary>What one machine is given.</summary>
public class MachineSize
{
    public int Vcpu { get; set; }
    public long MemoryMiB { get; set; }
    public List<PlannedDisk> Disks { get; set; } = [];

    /// <summary>
    /// True when some of it is not known from the plan, such as the disks of a
    /// clone that does not size them, which it takes from its template. What is
    /// known is still counted.
    /// </summary>
    public bool Incomplete { get; set; }
}

public class PlannedDisk
{
    /// <summary>
    /// The datastore it goes on: a Proxmox storage name, or a vSphere datastore's
    /// managed object id. Null when the plan does not say.
    /// </summary>
    public string Datastore { get; set; }

    public double SizeGiB { get; set; }
}

/// <summary>
/// What a host or datastore has left, read from the infrastructure inventory.
/// </summary>
public class HypervisorRoom
{
    public List<HostRoom> Hosts { get; set; } = [];
    public List<DatastoreRoom> Datastores { get; set; } = [];

    /// <summary>
    /// The providers whose hosts could be read. Room on a provider not listed is
    /// not checked, since nothing is known about it.
    /// </summary>
    public HashSet<string> HostsRead { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The providers whose datastores could be read, with their free space.</summary>
    public HashSet<string> DatastoresRead { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>When the oldest of the inventories used was read, in UTC.</summary>
    public DateTime? ReadAt { get; set; }

    /// <summary>Things the check should say about the inventory, such as a stale read.</summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>One Proxmox node or vSphere host, and how much more it can take.</summary>
public class HostRoom
{
    public string Connection { get; set; }
    public string Hypervisor { get; set; }

    /// <summary>The node name on Proxmox, the managed object id (<c>host-12</c>) on vSphere.</summary>
    public string Id { get; set; }
    public string Name { get; set; }

    /// <summary>False when the host is offline, disconnected or in maintenance: it has no room.</summary>
    public bool Online { get; set; }

    public int Cores { get; set; }

    /// <summary>
    /// The vCPUs the host's running VMs are given. When the inventory does not say,
    /// the cores in use now (its CPU load times its cores), rounded up, and
    /// <see cref="CpuApproximated"/> is set.
    /// </summary>
    public int AllocatedVcpu { get; set; }
    public bool CpuApproximated { get; set; }

    /// <summary>Cores times the overcommit ratio, less <see cref="AllocatedVcpu"/>, never below 0.</summary>
    public int FreeVcpu { get; set; }

    public long MemoryMiB { get; set; }
    public long UsedMemoryMiB { get; set; }

    /// <summary>Memory less the reserve, less what is in use, never below 0.</summary>
    public long FreeMemoryMiB { get; set; }
}

/// <summary>One datastore, and its free space less the reserve.</summary>
public class DatastoreRoom
{
    public string Connection { get; set; }
    public string Hypervisor { get; set; }

    /// <summary>The storage name on Proxmox, the managed object id (<c>datastore-12</c>) on vSphere.</summary>
    public string Id { get; set; }
    public string Name { get; set; }
    public double FreeGiB { get; set; }
}

/// <summary>Whether a run can be applied, as far as its size goes.</summary>
public enum CapacityCheckStatus
{
    /// <summary>Within every limit, and there is room.</summary>
    Ok,

    /// <summary>Within every limit, but the hypervisors may not have room: apply once acknowledged.</summary>
    Warning,

    /// <summary>Over a limit, or out of room with the room check set to Block. Not applied.</summary>
    Refused,

    /// <summary>The plan's machines could not be read, so nothing was checked.</summary>
    Unknown,
}

/// <summary>Where an effective limit comes from.</summary>
public enum LimitSource
{
    Global,
    Project,
    Workspace,
}

/// <summary>One effective limit, and where it is set.</summary>
public class EffectiveLimit
{
    public int Value { get; set; }
    public LimitSource Source { get; set; }
}

/// <summary>The lowest of a workspace's own limits, its project's and the global ones. A null is no limit.</summary>
public class EffectiveLimits
{
    public EffectiveLimit MaxMachines { get; set; }
    public EffectiveLimit MaxVcpu { get; set; }
    public EffectiveLimit MaxMemoryGiB { get; set; }
    public EffectiveLimit MaxDiskGiB { get; set; }

    [JsonIgnore]
    public bool Any => MaxMachines != null || MaxVcpu != null || MaxMemoryGiB != null || MaxDiskGiB != null;
}

/// <summary>Machines and what they are given between them.</summary>
public class CapacityTotals
{
    public int Machines { get; set; }
    public int Vcpu { get; set; }
    public long MemoryMiB { get; set; }
    public double DiskGiB { get; set; }

    /// <summary>Machines whose size is not wholly known from the plan; what is known of them is counted.</summary>
    public int Incomplete { get; set; }
}

/// <summary>A limit this run takes the workspace over.</summary>
public class CapacityLimitViolation
{
    /// <summary>MaxMachines, MaxVcpu, MaxMemoryGiB or MaxDiskGiB.</summary>
    public string Limit { get; set; }
    public int Value { get; set; }
    public LimitSource Source { get; set; }

    /// <summary>What the workspace would have, in the limit's unit.</summary>
    public double Total { get; set; }

    /// <summary>What it has before the run, in the limit's unit.</summary>
    public double Before { get; set; }

    public string Message { get; set; }
}

/// <summary>
/// The room one host (or, for machines the plan places on none, every host of
/// a provider between them) has for the machines this run adds or grows there.
/// </summary>
public class CapacityHostLine
{
    public string Hypervisor { get; set; }
    public string Connection { get; set; }

    /// <summary>The host's name. Null for the line that stands for every host of the provider.</summary>
    public string Host { get; set; }

    public bool Aggregate => Host == null;

    /// <summary>The addresses of the machines counted on this line.</summary>
    public List<string> Machines { get; set; } = [];

    public int NeedVcpu { get; set; }
    public long NeedMemoryMiB { get; set; }
    public int FreeVcpu { get; set; }
    public long FreeMemoryMiB { get; set; }

    /// <summary>The host's free vCPU is from its current load, not what its VMs are given.</summary>
    public bool CpuApproximated { get; set; }

    public bool Online { get; set; } = true;
    public bool Fits { get; set; }
    public string Message { get; set; }
}

/// <summary>The room one datastore, or every datastore of a provider between them, has for this run's new disk.</summary>
public class CapacityDatastoreLine
{
    public string Hypervisor { get; set; }
    public string Connection { get; set; }

    /// <summary>The datastore's name. Null for the line that stands for every datastore of the provider.</summary>
    public string Datastore { get; set; }

    public bool Aggregate => Datastore == null;
    public double NeedGiB { get; set; }
    public double FreeGiB { get; set; }
    public bool Fits { get; set; }
    public string Message { get; set; }
}

/// <summary>
/// The size check of one run: what it adds, what the workspace has after, the
/// limits, and the room on the hypervisors, host by host.
/// </summary>
public class CapacityCheck
{
    public CapacityCheckStatus Status { get; set; }

    public RoomCheckMode RoomCheck { get; set; }

    /// <summary>False when the run is refused.</summary>
    public bool CanApply { get; set; } = true;

    /// <summary>True when the run may be applied, but only once someone acknowledges the warning.</summary>
    public bool RequiresAcknowledgement { get; set; }

    /// <summary>One line to show next to Apply. Null when all is well.</summary>
    public string Message { get; set; }

    /// <summary>What the run adds: machines it creates, and the vCPU, memory and disk of those and of the machines it grows.</summary>
    public CapacityTotals Requested { get; set; } = new();

    /// <summary>The workspace's machines before the run.</summary>
    public CapacityTotals Before { get; set; } = new();

    /// <summary>The workspace's machines after the run.</summary>
    public CapacityTotals After { get; set; } = new();

    public EffectiveLimits Limits { get; set; } = new();

    public List<CapacityLimitViolation> LimitViolations { get; set; } = [];

    public List<CapacityHostLine> Hosts { get; set; } = [];

    public List<CapacityDatastoreLine> Datastores { get; set; } = [];

    /// <summary>What was not checked, and why.</summary>
    public List<string> Notes { get; set; } = [];

    public double CpuOvercommitRatio { get; set; }
    public double ReservePercent { get; set; }

    /// <summary>When the inventory the room is from was read, in UTC. Null when none was.</summary>
    public DateTime? InventoryReadAt { get; set; }
}
