// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Caster.Api.Domain.Models;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Caster.Api.Infrastructure.Authorization
{
    public class ProjectPermissionRequirement : IAuthorizationRequirement
    {
        public ProjectPermission[] RequiredPermissions;
        public Guid ProjectId;

        public ProjectPermissionRequirement(
            ProjectPermission[] requiredPermissions,
            Guid projectId)
        {
            RequiredPermissions = requiredPermissions;
            ProjectId = projectId;
        }
    }

    public class ProjectPermissionsHandler : AuthorizationHandler<ProjectPermissionRequirement>, IAuthorizationHandler
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ProjectPermissionRequirement requirement)
        {
            if (context.User == null)
            {
                context.Fail();
            }
            else
            {
                ProjectPermissionsClaim projectPermissionsClaim = null;

                var claims = context.User.Claims
                    .Where(x => x.Type == AuthorizationConstants.ProjectPermissionsClaimType)
                    .ToList();

                foreach (var claim in claims)
                {
                    var claimValue = ProjectPermissionsClaim.FromString(claim.Value);
                    if (claimValue.ProjectId == requirement.ProjectId)
                    {
                        projectPermissionsClaim = claimValue;
                        break;
                    }
                }

                if (projectPermissionsClaim == null)
                {
                    context.Fail();
                }
                else if (requirement.RequiredPermissions == null || requirement.RequiredPermissions.Length == 0)
                {
                    context.Succeed(requirement);
                }
                else if (requirement.RequiredPermissions.Any(x => projectPermissionsClaim.Permissions.Contains(x)))
                {
                    context.Succeed(requirement);
                }
            }

            return Task.CompletedTask;
        }
    }
}
