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
    /// <summary>The access of each project: its own scopes plus its project group's.</summary>
    Task<Dictionary<Guid, ModuleAccess>> ForProjects(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken);

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
        var direct = await dbContext.ProjectModuleScopes
            .Where(x => projectIds.Contains(x.ProjectId))
            .Select(x => new { x.ProjectId, x.ModuleScope.Paths })
            .ToListAsync(cancellationToken);

        var grouped = await dbContext.ProjectGroupModuleScopes
            .SelectMany(x => x.ProjectGroup.Projects, (link, project) => new { ProjectId = project.Id, link.ModuleScope.Paths })
            .Where(x => projectIds.Contains(x.ProjectId))
            .ToListAsync(cancellationToken);

        var scopes = direct.Concat(grouped).ToLookup(x => x.ProjectId);

        var options = terraformOptions.CurrentValue;
        var shared = options.SharedSourceNames();
        var restrictUnassigned = options?.ModuleScopes?.RestrictUnassigned ?? false;

        return projectIds.Distinct().ToDictionary(
            id => id,
            id => scopes.Contains(id)
                ? ModuleAccess.Of(scopes[id].SelectMany(x => x.Paths), shared)
                : restrictUnassigned ? ModuleAccess.Of([], shared) : ModuleAccess.Everything);
    }

    public async Task<ModuleAccess> ForProject(Guid projectId, CancellationToken cancellationToken) =>
        (await ForProjects([projectId], cancellationToken))[projectId];

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
