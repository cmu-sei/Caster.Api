using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Infrastructure.Authorization;

public interface ICasterAuthorizationService
{
    Task<bool> Authorize(
        SystemPermissions[] requiredSystemPermissions,
        CancellationToken cancellationToken);

    Task<bool> Authorize<T>(
        Guid? resourceId,
        SystemPermissions[] requiredSystemPermissions,
        ProjectPermissions[] requiredProjectPermissions,
        CancellationToken cancellationToken);
}

public class AuthorizationService(
    IAuthorizationService _authService,
    IIdentityResolver _identityResolver,
    CasterContext _db) : ICasterAuthorizationService
{
    public async Task<bool> Authorize(
        SystemPermissions[] requiredSystemPermissions,
        CancellationToken cancellationToken)
    {
        return await Authorize<object>(null, requiredSystemPermissions, [], cancellationToken);
    }

    public async Task<bool> Authorize<T>(
        Guid? resourceId,
        SystemPermissions[] requiredSystemPermissions,
        ProjectPermissions[] requiredProjectPermissions,
        CancellationToken cancellationToken)
    {
        var claimsPrincipal = _identityResolver.GetClaimsPrincipal();
        var permissionRequirement = new SystemPermissionsRequirement(requiredSystemPermissions);
        var permissionResult = await _authService.AuthorizeAsync(claimsPrincipal, null, permissionRequirement);

        if (permissionResult.Succeeded)
            return true;

        if (resourceId.HasValue)
        {
            var projectId = await this.GetProjectId<T>(resourceId.Value, cancellationToken);

            if (projectId == null)
                throw new ForbiddenException();

            var projectPermissionRequirement = new ProjectPermissionsRequirement(requiredProjectPermissions, projectId.Value);
            var projectPermissionResult = await _authService.AuthorizeAsync(claimsPrincipal, null, projectPermissionRequirement);

            if (!projectPermissionResult.Succeeded)
                throw new ForbiddenException();
        }
        else
        {
            throw new ForbiddenException();
        }

        return true;
    }

    private async Task<Guid?> GetProjectId<T>(Guid resourceId, CancellationToken cancellationToken)
    {
        Guid? projectId = null;

        switch (typeof(T))
        {
            case Type t when t == typeof(Project):
                projectId = resourceId;
                break;
            case Type t when t == typeof(Run):
                projectId = await HandleRun(resourceId, cancellationToken);
                break;
            default:
                break;
        }

        return projectId;
    }

    private async Task<Guid> HandleRun(Guid runId, CancellationToken cancellationToken)
    {
        return await _db.Runs
            .Where(x => x.Id == runId)
            .Select(x => x.Workspace.Directory.ProjectId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}