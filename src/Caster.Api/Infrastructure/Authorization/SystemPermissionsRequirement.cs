// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Caster.Api.Domain.Models;
using Microsoft.AspNetCore.Authorization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Caster.Api.Infrastructure.Authorization
{
    public class SystemPermissionsRequirement : IAuthorizationRequirement
    {
        public SystemPermissions[] RequiredPermissions;
        public AuthorizationType AuthorizationType;

        public SystemPermissionsRequirement(
            SystemPermissions[] requiredPermissions,
            AuthorizationType authorizationType)
        {
            RequiredPermissions = requiredPermissions;
            AuthorizationType = authorizationType;
        }
    }

    public class SystemPermissionsHandler : AuthorizationHandler<SystemPermissionsRequirement>, IAuthorizationHandler
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SystemPermissionsRequirement requirement)
        {
            if (context.User == null)
            {
                context.Fail();
            }
            else if (requirement.RequiredPermissions == null || requirement.RequiredPermissions.Length == 0)
            {
                context.Succeed(requirement);
            }
            else if (context.User.HasClaim(AuthorizationConstants.PermissionsClaimType, SystemPermissions.All.ToString()))
            {
                context.Succeed(requirement);
            }
            // else if (requirement.AuthorizationType == AuthorizationType.Write &&
            //     context.User.HasClaim(AuthorizationConstants.PermissionsClaimType, SystemPermissions.ReadOnly.ToString()))
            // {
            //     context.Fail();
            // }
            else if (requirement.RequiredPermissions.Any(p => context.User.HasClaim(AuthorizationConstants.PermissionsClaimType, p.ToString())))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
