// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Caster.Api.Infrastructure.Options;

/// <summary>
/// How much one workspace may deploy, and how a run is checked against the
/// room left on the hypervisors before it is applied. Bound from <c>Capacity</c>.
/// </summary>
public class CapacityOptions
{
    /// <summary>The most machines one workspace may have. Null for no limit.</summary>
    public int? MaxMachines { get; set; }

    /// <summary>The most vCPUs one workspace's machines may have between them. Null for no limit.</summary>
    public int? MaxVcpu { get; set; }

    /// <summary>The most memory, in GiB, one workspace's machines may have between them. Null for no limit.</summary>
    public int? MaxMemoryGiB { get; set; }

    /// <summary>The most disk, in GiB, one workspace's machines may have between them. Null for no limit.</summary>
    public int? MaxDiskGiB { get; set; }

    /// <summary>
    /// How many vCPUs a host may give its VMs for each core it has. A host with
    /// 16 cores and a ratio of 4 has room for 64 vCPUs, less what its running
    /// VMs are given.
    /// </summary>
    public double CpuOvercommitRatio { get; set; } = 4;

    /// <summary>
    /// The share of each host's memory and each datastore's free space, in
    /// percent, that is kept back and never counted as room.
    /// </summary>
    public double ReservePercent { get; set; } = 10;

    /// <summary>What happens when a run needs more room than the hypervisors have.</summary>
    public RoomCheckMode RoomCheck { get; set; } = RoomCheckMode.Warn;
}

public enum RoomCheckMode
{
    /// <summary>The run is applied once someone acknowledges the warning.</summary>
    Warn,

    /// <summary>The run is not applied.</summary>
    Block,
}
