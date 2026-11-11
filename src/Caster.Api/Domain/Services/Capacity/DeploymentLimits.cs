// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Caster.Api.Infrastructure.Options;

namespace Caster.Api.Domain.Services.Capacity;

/// <summary>The four limits, as a project or workspace sets them. A null is no limit at that level.</summary>
public interface IDeploymentLimits
{
    int? MaxMachines { get; }
    int? MaxVcpu { get; }
    int? MaxMemoryGiB { get; }
    int? MaxDiskGiB { get; }
}

/// <summary>A project or workspace, whose limits can be set.</summary>
public interface IDeploymentLimitsTarget
{
    int? MaxMachines { set; }
    int? MaxVcpu { set; }
    int? MaxMemoryGiB { set; }
    int? MaxDiskGiB { set; }
}

public static class DeploymentLimitResolver
{
    /// <summary>
    /// The lowest of each limit the global settings, the project and the
    /// workspace set. One that none sets is no limit. On a tie the narrowest
    /// level is named, since that is where it would be changed.
    /// </summary>
    public static EffectiveLimits Resolve(CapacityOptions global, IDeploymentLimits project, IDeploymentLimits workspace) => new()
    {
        MaxMachines = Lowest(global?.MaxMachines, project?.MaxMachines, workspace?.MaxMachines),
        MaxVcpu = Lowest(global?.MaxVcpu, project?.MaxVcpu, workspace?.MaxVcpu),
        MaxMemoryGiB = Lowest(global?.MaxMemoryGiB, project?.MaxMemoryGiB, workspace?.MaxMemoryGiB),
        MaxDiskGiB = Lowest(global?.MaxDiskGiB, project?.MaxDiskGiB, workspace?.MaxDiskGiB),
    };

    private static EffectiveLimit Lowest(int? global, int? project, int? workspace)
    {
        EffectiveLimit lowest = null;

        foreach (var (value, source) in new[] { (workspace, LimitSource.Workspace), (project, LimitSource.Project), (global, LimitSource.Global) })
        {
            if (value is { } v && v >= 0 && (lowest == null || v < lowest.Value))
                lowest = new EffectiveLimit { Value = v, Source = source };
        }

        return lowest;
    }
}
