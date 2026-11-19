// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Services.ModuleScopes;
using Caster.Api.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.ModuleScopes;

/// <summary>Builds the views of Module Scopes, written out because a view lists names from three tables.</summary>
public static class ModuleScopeViews
{
    public static async Task<ModuleScope[]> GetScopes(
        this CasterContext dbContext,
        Expression<Func<Domain.Models.ModuleScope, bool>> filter,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ModuleScopes.AsNoTracking();

        if (filter is not null)
            query = query.Where(filter);

        var scopes = await query
            .Include(x => x.Projects).ThenInclude(x => x.Project)
            .Include(x => x.ProjectGroups).ThenInclude(x => x.ProjectGroup)
            .AsSplitQuery()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return scopes
            .Select(x => new ModuleScope
            {
                Id = x.Id,
                Name = x.Name,
                Description = x.Description,
                Paths = x.Paths.Order(StringComparer.Ordinal).ToArray(),
                Projects = x.Projects
                    .Select(p => new ModuleScopeProject { ProjectId = p.ProjectId, Name = p.Project?.Name })
                    .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                ProjectGroups = x.ProjectGroups
                    .Select(g => new ModuleScopeProjectGroup { ProjectGroupId = g.ProjectGroupId, Name = g.ProjectGroup?.Name })
                    .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            })
            .ToArray();
    }

    public static async Task<ModuleScope> GetScope(this CasterContext dbContext, Guid id, CancellationToken cancellationToken) =>
        (await dbContext.GetScopes(x => x.Id == id, cancellationToken)).FirstOrDefault();

    public static async Task<ProjectModuleAccess> GetProjectAccess(
        this CasterContext dbContext, Guid projectId, TerraformOptions options, CancellationToken cancellationToken)
    {
        var direct = await dbContext.ProjectModuleScopes
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .Select(x => new { x.ModuleScope, GroupId = (Guid?)null, GroupName = (string)null })
            .ToListAsync(cancellationToken);

        var grouped = await dbContext.ProjectGroupModuleScopes
            .AsNoTracking()
            .Where(x => x.ProjectGroup.Projects.Any(p => p.Id == projectId))
            .Select(x => new { x.ModuleScope, GroupId = (Guid?)x.ProjectGroupId, GroupName = x.ProjectGroup.Name })
            .ToListAsync(cancellationToken);

        var scopes = direct.Concat(grouped)
            .GroupBy(x => x.ModuleScope.Id)
            .Select(g => new ProjectModuleAccessScope
            {
                ModuleScopeId = g.Key,
                Name = g.First().ModuleScope.Name,
                Description = g.First().ModuleScope.Description,
                Paths = g.First().ModuleScope.Paths.Order(StringComparer.Ordinal).ToArray(),
                IsDirect = g.Any(x => x.GroupId is null),
                ProjectGroups = g.Where(x => x.GroupId.HasValue)
                    .Select(x => new ModuleScopeProjectGroup { ProjectGroupId = x.GroupId.Value, Name = x.GroupName })
                    .ToArray()
            })
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var assigned = scopes.Length > 0;
        var restrictUnassigned = options?.ModuleScopes?.RestrictUnassigned ?? false;
        var paths = ModuleAccess.Of(scopes.SelectMany(x => x.Paths)).Paths;

        return new ProjectModuleAccess
        {
            ProjectId = projectId,
            Restricted = assigned || restrictUnassigned,
            RestrictedBecauseUnassigned = !assigned && restrictUnassigned,
            Paths = paths,
            SharedSources = options.SharedSourceNames(),
            Scopes = scopes,
            PathOrigins = paths
                .Select(path => new ProjectModuleAccessPath
                {
                    Path = path,
                    Scopes = scopes.Where(x => x.Paths.Contains(path)).ToArray()
                })
                .ToArray()
        };
    }
}
