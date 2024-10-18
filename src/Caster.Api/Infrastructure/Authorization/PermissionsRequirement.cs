// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Caster.Api.Domain.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Caster.Api.Infrastructure.Authorization
{
    public class PermissionsRequirement : IAuthorizationRequirement
    {
        public SystemPermissions RequiredPermission;

        public PermissionsRequirement(SystemPermissions requiredPermission)
        {
            RequiredPermission = requiredPermission;
        }
    }

    public class PermissionsHandler : AuthorizationHandler<PermissionsRequirement>, IAuthorizationHandler
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionsRequirement requirement)
        {
            if (context.User != null && context.User.HasClaim("Permission", requirement.RequiredPermission.ToString()))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
