// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Domain.Services.ProjectGroups;

/// <summary>
/// An Environment Set a project gets: assigned to it directly (<see cref="ProjectGroupId"/> null),
/// or through its project group.
/// </summary>
public class EffectiveSetAssignment
{
    public Guid ProjectId { get; set; }
    public Guid EnvironmentSetId { get; set; }
    public Guid? ProjectGroupId { get; set; }
}

/// <summary>An inventory connection a project sees, directly or through its group.</summary>
public class EffectiveConnectionAssignment
{
    public Guid ProjectId { get; set; }
    public string ConnectionName { get; set; }
    public string NormalizedConnectionName { get; set; }
    public Guid? ProjectGroupId { get; set; }
}

/// <summary>
/// What a project has: its own assignments plus its group's. Every reader of a project's sets,
/// connections, partition or hosts goes through here, so group resolution is written once.
/// </summary>
public static class ProjectGroupResolution
{
    public static IQueryable<EffectiveSetAssignment> EffectiveSetAssignments(this CasterContext dbContext) =>
        dbContext.ProjectEnvironmentSets
            .Select(x => new EffectiveSetAssignment { ProjectId = x.ProjectId, EnvironmentSetId = x.EnvironmentSetId, ProjectGroupId = null })
            .Concat(dbContext.ProjectGroupEnvironmentSets
                .SelectMany(x => x.ProjectGroup.Projects, (link, project) => new EffectiveSetAssignment
                {
                    ProjectId = project.Id,
                    EnvironmentSetId = link.EnvironmentSetId,
                    ProjectGroupId = (Guid?)link.ProjectGroupId
                }));

    public static IQueryable<EffectiveConnectionAssignment> EffectiveConnectionAssignments(this CasterContext dbContext) =>
        dbContext.ProjectInventoryConnections
            .Select(x => new EffectiveConnectionAssignment
            {
                ProjectId = x.ProjectId,
                ConnectionName = x.ConnectionName,
                NormalizedConnectionName = x.NormalizedConnectionName,
                ProjectGroupId = null
            })
            .Concat(dbContext.ProjectGroupInventoryConnections
                .SelectMany(x => x.ProjectGroup.Projects, (link, project) => new EffectiveConnectionAssignment
                {
                    ProjectId = project.Id,
                    ConnectionName = link.ConnectionName,
                    NormalizedConnectionName = link.NormalizedConnectionName,
                    ProjectGroupId = (Guid?)link.ProjectGroupId
                }));

    /// <summary>The project's own partition, else its group's, else null.</summary>
    public static Task<Guid?> ProjectPartitionId(this CasterContext dbContext, Guid projectId, CancellationToken cancellationToken) =>
        dbContext.Projects
            .Where(x => x.Id == projectId)
            .Select(x => x.PartitionId ?? (x.ProjectGroup == null ? null : x.ProjectGroup.PartitionId))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>The hosts a project's runs can be placed on: its own and its group's.</summary>
    public static IQueryable<Models.Host> ProjectHosts(this CasterContext dbContext, Guid projectId) =>
        dbContext.Hosts.Where(h =>
            h.ProjectId == projectId ||
            (h.ProjectGroupId != null && dbContext.Projects.Any(p => p.Id == projectId && p.ProjectGroupId == h.ProjectGroupId)));
}
