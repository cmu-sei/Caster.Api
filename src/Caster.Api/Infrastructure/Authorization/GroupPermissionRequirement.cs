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
    public class GroupPermissionRequirement : IAuthorizationRequirement
    {
        public GroupPermission[] RequiredPermissions;
        public Guid GroupId;

        public GroupPermissionRequirement(
            GroupPermission[] requiredPermissions,
            Guid groupId)
        {
            RequiredPermissions = requiredPermissions;
            GroupId = groupId;
        }
    }

    public class GroupPermissionsHandler : AuthorizationHandler<GroupPermissionRequirement>, IAuthorizationHandler
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, GroupPermissionRequirement requirement)
        {
            if (context.User == null)
            {
                context.Fail();
            }
            else
            {
                GroupPermissionsClaim groupPermissionsClaim = null;

                var claims = context.User.Claims
                    .Where(x => x.Type == AuthorizationConstants.GroupPermissionsClaimType)
                    .ToList();

                foreach (var claim in claims)
                {
                    var claimValue = GroupPermissionsClaim.FromString(claim.Value);
                    if (claimValue.GroupId == requirement.GroupId)
                    {
                        groupPermissionsClaim = claimValue;
                        break;
                    }
                }

                if (groupPermissionsClaim == null)
                {
                    context.Fail();
                }
                else if (requirement.RequiredPermissions == null || requirement.RequiredPermissions.Length == 0)
                {
                    context.Succeed(requirement);
                }
                else if (requirement.RequiredPermissions.Any(x => groupPermissionsClaim.Permissions.Contains(x)))
                {
                    context.Succeed(requirement);
                }
            }

            return Task.CompletedTask;
        }
    }
}
