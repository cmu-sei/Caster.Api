using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
        AuthorizationType authType,
        SystemPermissions[] requiredSystemPermissions);

    Task<bool> Authorize<T>(
        Guid? resourceId,
        AuthorizationType authType,
        SystemPermissions[] requiredSystemPermissions,
        ProjectPermissions[] requiredProjectPermissions);
}

public class AuthorizationService(
    IAuthorizationService _authService,
    IIdentityResolver _identityResolver,
    CasterContext _db) : ICasterAuthorizationService
{
    public async Task<bool> Authorize(
        AuthorizationType authType,
        SystemPermissions[] requiredSystemPermissions)
    {
        return await Authorize<object>(null, authType, requiredSystemPermissions, []);
    }

    public async Task<bool> Authorize<T>(Guid? resourceId, AuthorizationType authType, SystemPermissions[] requiredSystemPermissions, ProjectPermissions[] requiredProjectPermissions)
    {
        var claimsPrincipal = _identityResolver.GetClaimsPrincipal();
        var permissionRequirement = new SystemPermissionsRequirement(requiredSystemPermissions, authType);
        var permissionResult = await _authService.AuthorizeAsync(claimsPrincipal, null, permissionRequirement);

        if (permissionResult.Succeeded)
            return true;

        if (resourceId.HasValue)
        {
            var projectId = await this.GetProjectId<T>(resourceId.Value);

            if (projectId == null)
                throw new ForbiddenException();

            var projectPermissionRequirement = new ProjectPermissionsRequirement(requiredProjectPermissions, authType, projectId.Value);
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

    private async Task<Guid?> GetProjectId<T>(Guid resourceId)
    {
        Guid? projectId = null;

        switch (typeof(T))
        {
            case Type t when t == typeof(Project):
                projectId = resourceId;
                break;
            case Type t when t == typeof(Run):
                projectId = await HandleRun(resourceId);
                break;
            default:
                break;
        }

        return projectId;
    }

    private async Task<Guid> HandleRun(Guid runId)
    {
        return await _db.Runs
            .Where(x => x.Id == runId)
            .Select(x => x.Workspace.Directory.ProjectId)
            .FirstOrDefaultAsync();
    }
}

public enum AuthorizationType
{
    Read,
    Write
}