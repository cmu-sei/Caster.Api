// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Caster.Api.Domain.Services.ModuleScopes;

/// <summary>
/// Which Modules a project, or the caller, may use. Every endpoint that lists or looks up Modules asks here.
/// The shared sources are always allowed. A project with no scope is allowed every Module, or only the
/// shared sources when <c>Terraform:ModuleScopes:RestrictUnassigned</c> is on.
/// </summary>
public interface IModuleAccessService
{
    /// <summary>
    /// The access of each project that exists: its own scopes plus its project group's, read in one
    /// query. An id with no project is left out, so it never counts as an unrestricted project.
    /// </summary>
    Task<Dictionary<Guid, ModuleAccess>> ForProjects(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken);

    /// <summary>The access of the project; throws <see cref="EntityNotFoundException{T}"/> when there is no such project.</summary>
    Task<ModuleAccess> ForProject(Guid projectId, CancellationToken cancellationToken);

    /// <summary>
    /// What the caller may use for a request that names <paramref name="projectId"/>, or none: that
    /// project's access; with no project, everything for a holder of ViewModules (the Modules
    /// admin page), else what the caller's own projects together allow.
    /// </summary>
    Task<ModuleAccess> ForCaller(Guid? projectId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the caller may look up the Module. A Module the caller's project (or any of
    /// their projects, when none is named) already uses in a Design can always be looked up, so
    /// that Design keeps working after the scope changes.
    /// </summary>
    Task<bool> CanSeeModule(Guid moduleId, Guid? projectId, CancellationToken cancellationToken);
}

public class ModuleAccessService(
    CasterContext dbContext,
    ICasterAuthorizationService authorizationService,
    IOptionsMonitor<TerraformOptions> terraformOptions) : IModuleAccessService
{
    public async Task<Dictionary<Guid, ModuleAccess>> ForProjects(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken)
    {
        // One statement reads the projects, their own scopes and their group's, so the decision
        // "no scope applies" is made from one consistent snapshot: moving an assignment between a
        // project and its group, or a project between groups, is never seen half done. Only
        // projects that exist are answered; an id with no row (a deleted project still in cached
        // claims, a made-up id) contributes nothing and is never treated as unassigned.
        var projects = await dbContext.Projects
            .AsNoTracking()
            .Where(x => projectIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                Own = x.ModuleScopes.Select(s => s.ModuleScope.Paths).ToList(),
                Inherited = x.ProjectGroup == null
                    ? null
                    : x.ProjectGroup.ModuleScopes.Select(s => s.ModuleScope.Paths).ToList()
            })
            .ToListAsync(cancellationToken);

        var options = terraformOptions.CurrentValue;
        var shared = options.SharedSourceNames();
        var restrictUnassigned = options?.ModuleScopes?.RestrictUnassigned ?? false;

        return projects.ToDictionary(
            x => x.Id,
            x =>
            {
                var paths = x.Own.Concat(x.Inherited ?? []).SelectMany(p => p).ToList();
                var assigned = x.Own.Count > 0 || (x.Inherited?.Count ?? 0) > 0;

                return assigned
                    ? ModuleAccess.Of(paths, shared)
                    : restrictUnassigned ? ModuleAccess.Of([], shared) : ModuleAccess.Everything;
            });
    }

    public async Task<ModuleAccess> ForProject(Guid projectId, CancellationToken cancellationToken) =>
        (await ForProjects([projectId], cancellationToken)).TryGetValue(projectId, out var access)
            ? access
            : throw new EntityNotFoundException<Project>();

    public async Task<ModuleAccess> ForCaller(Guid? projectId, CancellationToken cancellationToken)
    {
        if (projectId.HasValue)
            return await ForProject(projectId.Value, cancellationToken);

        if (await authorizationService.Authorize([SystemPermission.ViewModules], cancellationToken))
            return ModuleAccess.Everything;

        var projectIds = authorizationService.GetAuthorizedProjectIds().Distinct().ToList();
        return ModuleAccess.Union((await ForProjects(projectIds, cancellationToken)).Values, terraformOptions.CurrentValue.SharedSourceNames());
    }

    public async Task<bool> CanSeeModule(Guid moduleId, Guid? projectId, CancellationToken cancellationToken)
    {
        var path = await dbContext.Modules
            .Where(x => x.Id == moduleId)
            .Select(x => x.Path)
            .FirstOrDefaultAsync(cancellationToken);

        if (path is null)
            return false;

        var access = await ForCaller(projectId, cancellationToken);

        if (access.Allows(path))
            return true;

        var projectIds = projectId.HasValue
            ? [projectId.Value]
            : authorizationService.GetAuthorizedProjectIds().Distinct().ToList();

        return await dbContext.DesignModules.AnyAsync(
            x => x.ModuleId == moduleId && projectIds.Contains(x.Design.Directory.ProjectId), cancellationToken);
    }
}
