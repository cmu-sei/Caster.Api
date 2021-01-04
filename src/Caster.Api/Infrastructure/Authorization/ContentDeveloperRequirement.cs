// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Caster.Api.Infrastructure.Authorization
{
    public class ContentDeveloperRequirement : IAuthorizationRequirement
    {
        public ContentDeveloperRequirement()
        {
        }
    }

    public class ContentDeveloperHandler : AuthorizationHandler<ContentDeveloperRequirement>, IAuthorizationHandler
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ContentDeveloperRequirement requirement)
        {
            if (context.User.HasClaim(ClaimTypes.Role, nameof(CasterClaimTypes.SystemAdmin)) ||
                context.User.HasClaim(ClaimTypes.Role, nameof(CasterClaimTypes.ContentDeveloper)))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
