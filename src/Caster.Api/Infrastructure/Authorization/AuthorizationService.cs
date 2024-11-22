using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Infrastructure.Authorization;

public interface ICasterAuthorizationService
{
    Task<bool> Authorize(
        SystemPermission[] requiredSystemPermissions,
        CancellationToken cancellationToken);

    Task<bool> Authorize<T>(
        Guid? resourceId,
        SystemPermission[] requiredSystemPermissions,
        ProjectPermission[] requiredProjectPermissions,
        CancellationToken cancellationToken) where T : IEntity;

    IEnumerable<Guid> GetAuthorizedProjectIds();
    IEnumerable<SystemPermission> GetSystemPermissions();
    IEnumerable<ProjectPermissionsClaim> GetProjectPermissions(Guid? projectId);
}

public class AuthorizationService(
    IAuthorizationService authService,
    IIdentityResolver identityResolver,
    CasterContext dbContext) : ICasterAuthorizationService
{
    public async Task<bool> Authorize(
        SystemPermission[] requiredSystemPermissions,
        CancellationToken cancellationToken)
    {
        return await Authorize<IEntity>(null, requiredSystemPermissions, [], cancellationToken);
    }

    public async Task<bool> Authorize<T>(
        Guid? resourceId,
        SystemPermission[] requiredSystemPermissions,
        ProjectPermission[] requiredProjectPermissions,
        CancellationToken cancellationToken) where T : IEntity
    {
        bool succeeded = false;
        var claimsPrincipal = identityResolver.GetClaimsPrincipal();
        var permissionRequirement = new SystemPermissionRequirement(requiredSystemPermissions);
        var permissionResult = await authService.AuthorizeAsync(claimsPrincipal, null, permissionRequirement);

        if (permissionResult.Succeeded)
            succeeded = true;

        if (!succeeded && resourceId.HasValue)
        {
            var projectId = await GetProjectId<T>(resourceId.Value, cancellationToken);

            if (projectId == null)
            {
                succeeded = false;
            }
            else
            {
                var projectPermissionRequirement = new ProjectPermissionRequirement(requiredProjectPermissions, projectId.Value);
                var projectPermissionResult = await authService.AuthorizeAsync(claimsPrincipal, null, projectPermissionRequirement);

                succeeded = projectPermissionResult.Succeeded;
            }

        }

        return succeeded;
    }

    public IEnumerable<Guid> GetAuthorizedProjectIds()
    {
        return identityResolver.GetClaimsPrincipal().Claims
            .Where(x => x.Type == AuthorizationConstants.ProjectPermissionsClaimType)
            .Select(x => ProjectPermissionsClaim.FromString(x.Value).ProjectId)
            .ToList();
    }

    public IEnumerable<SystemPermission> GetSystemPermissions()
    {
        return identityResolver.GetClaimsPrincipal().Claims
           .Where(x => x.Type == AuthorizationConstants.PermissionsClaimType)
           .Select(x =>
           {
               if (Enum.TryParse<SystemPermission>(x.Value, out var permission))
                   return permission;

               return (SystemPermission?)null;
           })
           .Where(x => x.HasValue)
           .Select(x => x.Value)
           .ToList();
    }

    public IEnumerable<ProjectPermissionsClaim> GetProjectPermissions(Guid? projectId)
    {
        var permissions = identityResolver.GetClaimsPrincipal().Claims
           .Where(x => x.Type == AuthorizationConstants.ProjectPermissionsClaimType)
           .Select(x => ProjectPermissionsClaim.FromString(x.Value));

        if (projectId.HasValue)
        {
            permissions = permissions.Where(x => x.ProjectId == projectId.Value);
        }

        return permissions;
    }

    private async Task<Guid?> GetProjectId<T>(Guid resourceId, CancellationToken cancellationToken)
    {
        return typeof(T) switch
        {
            var t when t == typeof(Project) => resourceId,
            var t when t == typeof(Directory) => await HandleDirectory(resourceId, cancellationToken),
            var t when t == typeof(File) => await HandleFile(resourceId, cancellationToken),
            var t when t == typeof(FileVersion) => await HandleFileVersion(resourceId, cancellationToken),
            var t when t == typeof(Workspace) => await HandleWorkspace(resourceId, cancellationToken),
            var t when t == typeof(Run) => await HandleRun(resourceId, cancellationToken),
            var t when t == typeof(Plan) => await HandlePlan(resourceId, cancellationToken),
            var t when t == typeof(Apply) => await HandleApply(resourceId, cancellationToken),
            var t when t == typeof(Design) => await HandleDesign(resourceId, cancellationToken),
            var t when t == typeof(DesignModule) => await HandleDesignModule(resourceId, cancellationToken),
            var t when t == typeof(Variable) => await HandleVariable(resourceId, cancellationToken),
            var t when t == typeof(ProjectMembership) => await HandleProjectMembership(resourceId, cancellationToken),
            _ => throw new NotImplementedException($"Handler for type {typeof(T).Name} is not implemented.")
        };
    }

    private async Task<Guid> HandleDirectory(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.Directories
            .Where(x => x.Id == id)
            .Select(x => x.ProjectId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Guid> HandleFile(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.Files
            .Where(x => x.Id == id)
            .Select(x => x.Directory.ProjectId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Guid> HandleFileVersion(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.Files
            .Where(x => x.Id == id)
            .Select(x => x.Directory.ProjectId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Guid> HandleWorkspace(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.Workspaces
            .Where(x => x.Id == id)
            .Select(x => x.Directory.ProjectId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Guid> HandleRun(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.Runs
            .Where(x => x.Id == id)
            .Select(x => x.Workspace.Directory.ProjectId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Guid> HandlePlan(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.Plans
            .Where(x => x.Id == id)
            .Select(x => x.Run.Workspace.Directory.ProjectId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Guid> HandleApply(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.Applies
            .Where(x => x.Id == id)
            .Select(x => x.Run.Workspace.Directory.ProjectId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Guid> HandleDesign(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.Designs
            .Where(x => x.Id == id)
            .Select(x => x.Directory.ProjectId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Guid> HandleDesignModule(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.DesignModules
            .Where(x => x.Id == id)
            .Select(x => x.Design.Directory.ProjectId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Guid> HandleVariable(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.Variables
            .Where(x => x.Id == id)
            .Select(x => x.Design.Directory.ProjectId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Guid> HandleProjectMembership(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.ProjectMemberships
            .Where(x => x.Id == id)
            .Select(x => x.ProjectId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}