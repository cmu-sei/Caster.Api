// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Infrastructure.Serialization;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Domain.Services.Capacity;

public interface ICapacityCheckService
{
    /// <summary>
    /// A run's plan checked against its workspace's effective limits and the
    /// room on the hypervisors now. Null when the run has no plan.
    /// </summary>
    Task<CapacityCheck> CheckRunAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>Any machines checked against a workspace's effective limits and the room now.</summary>
    Task<CapacityCheck> CheckAsync(Guid workspaceId, DeploymentDemand demand, CancellationToken cancellationToken);

    /// <summary>The lowest of the workspace's, its project's and the global limits.</summary>
    Task<EffectiveLimits> GetWorkspaceLimitsAsync(Guid workspaceId, CancellationToken cancellationToken);

    /// <summary>The lowest of the project's and the global limits: what a new workspace in it would have.</summary>
    Task<EffectiveLimits> GetProjectLimitsAsync(Guid projectId, CancellationToken cancellationToken);

    /// <summary>The room on every host and datastore the inventory knows of, now.</summary>
    HypervisorRoom ReadRoom();
}

public class CapacityCheckService(CasterContext db, CapacityOptions options, IHypervisorRoomSource roomSource) : ICapacityCheckService
{
    public async Task<CapacityCheck> CheckRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await db.Runs
            .Where(x => x.Id == runId)
            .Select(x => new { x.WorkspaceId, HasPlan = x.Plan != null, x.Plan.CapacityDemand })
            .SingleOrDefaultAsync(cancellationToken);

        if (run == null || !run.HasPlan)
            return null;

        return await CheckAsync(run.WorkspaceId, Read(run.CapacityDemand), cancellationToken);
    }

    public async Task<CapacityCheck> CheckAsync(Guid workspaceId, DeploymentDemand demand, CancellationToken cancellationToken)
    {
        var limits = await GetWorkspaceLimitsAsync(workspaceId, cancellationToken);
        return CapacityChecker.Check(demand, limits, demand == null ? null : ReadRoom(), options);
    }

    public async Task<EffectiveLimits> GetWorkspaceLimitsAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var workspace = await db.Workspaces
            .Where(x => x.Id == workspaceId)
            .Select(x => new
            {
                Workspace = new Limits(x.MaxMachines, x.MaxVcpu, x.MaxMemoryGiB, x.MaxDiskGiB),
                Project = new Limits(
                    x.Directory.Project.MaxMachines, x.Directory.Project.MaxVcpu,
                    x.Directory.Project.MaxMemoryGiB, x.Directory.Project.MaxDiskGiB),
            })
            .SingleOrDefaultAsync(cancellationToken);

        return DeploymentLimitResolver.Resolve(options, workspace?.Project, workspace?.Workspace);
    }

    public async Task<EffectiveLimits> GetProjectLimitsAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await db.Projects
            .Where(x => x.Id == projectId)
            .Select(x => new Limits(x.MaxMachines, x.MaxVcpu, x.MaxMemoryGiB, x.MaxDiskGiB))
            .SingleOrDefaultAsync(cancellationToken);

        return DeploymentLimitResolver.Resolve(options, project, null);
    }

    public HypervisorRoom ReadRoom() => roomSource.Read(options);

    /// <summary>A demand that cannot be read is the same as none: nothing is checked.</summary>
    public static DeploymentDemand Read(string json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<DeploymentDemand>(json, DefaultJsonSettings.Settings);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Limits(int? MaxMachines, int? MaxVcpu, int? MaxMemoryGiB, int? MaxDiskGiB) : IDeploymentLimits;
}
