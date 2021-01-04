// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Authorization;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Caster.Api.Infrastructure.Authorization
{
    public class BaseUserRequirement : IAuthorizationRequirement
    {
    }

    public class BaseUserHandler : AuthorizationHandler<BaseUserRequirement>, IAuthorizationHandler
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, BaseUserRequirement requirement)
        {
            if (context.User.HasClaim(ClaimTypes.Role, nameof(CasterClaimTypes.SystemAdmin)) ||
                context.User.HasClaim(ClaimTypes.Role, nameof(CasterClaimTypes.ContentDeveloper)) ||
                context.User.HasClaim(ClaimTypes.Role, nameof(CasterClaimTypes.Operator)) ||
                context.User.HasClaim(ClaimTypes.Role, nameof(CasterClaimTypes.BaseUser)))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
