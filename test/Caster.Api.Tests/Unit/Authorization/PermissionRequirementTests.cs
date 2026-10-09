// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Caster.Api.Tests.Unit.Authorization;

public class PermissionRequirementTests
{
    private static readonly Guid ScopeId = Guid.NewGuid();

    [Theory]
    [InlineData("System", true)]
    [InlineData("System", false)]
    [InlineData("Project", true)]
    [InlineData("Project", false)]
    [InlineData("Group", true)]
    [InlineData("Group", false)]
    public async Task Null_or_empty_requirements_do_not_succeed_even_with_permission_claims(string kind, bool nullRequirements)
    {
        IAuthorizationRequirement requirement = kind switch
        {
            "System" => new SystemPermissionRequirement(nullRequirements ? null : []),
            "Project" => new ProjectPermissionRequirement(nullRequirements ? null : [], ScopeId),
            _ => new GroupPermissionRequirement(nullRequirements ? null : [], ScopeId)
        };
        var context = new AuthorizationHandlerContext([requirement], Principal(), null);

        await Handler(kind).HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Theory]
    [InlineData("System", true)]
    [InlineData("System", false)]
    [InlineData("Project", true)]
    [InlineData("Project", false)]
    [InlineData("Group", true)]
    [InlineData("Group", false)]
    public async Task Nonempty_requirements_need_one_matching_permission(string kind, bool hasPermission)
    {
        IAuthorizationRequirement requirement = kind switch
        {
            "System" => new SystemPermissionRequirement([SystemPermission.ViewProjects, SystemPermission.EditProjects]),
            "Project" => new ProjectPermissionRequirement([ProjectPermission.ViewProject, ProjectPermission.EditProject], ScopeId),
            _ => new GroupPermissionRequirement([GroupPermission.ManageMembership, GroupPermission.EditGroup], ScopeId)
        };
        var principal = hasPermission ? Principal() : new ClaimsPrincipal(new ClaimsIdentity([], "Test"));
        var context = new AuthorizationHandlerContext([requirement], principal, null);

        await Handler(kind).HandleAsync(context);

        Assert.Equal(hasPermission, context.HasSucceeded);
    }

    [Theory]
    [InlineData("Project")]
    [InlineData("Group")]
    public async Task Matching_permissions_for_a_different_scope_do_not_succeed(string kind)
    {
        IAuthorizationRequirement requirement = kind == "Project"
            ? new ProjectPermissionRequirement([ProjectPermission.EditProject], Guid.NewGuid())
            : new GroupPermissionRequirement([GroupPermission.EditGroup], Guid.NewGuid());
        var context = new AuthorizationHandlerContext([requirement], Principal(), null);

        await Handler(kind).HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    private static IAuthorizationHandler Handler(string kind) => kind switch
    {
        "System" => new SystemPermissionsHandler(),
        "Project" => new ProjectPermissionsHandler(),
        _ => new GroupPermissionsHandler()
    };

    private static ClaimsPrincipal Principal() => new(new ClaimsIdentity(
    [
        new Claim(AuthorizationConstants.PermissionsClaimType, SystemPermission.EditProjects.ToString()),
        new Claim(AuthorizationConstants.ProjectPermissionsClaimType,
            new ProjectPermissionsClaim { ProjectId = ScopeId, Permissions = [ProjectPermission.EditProject] }.ToString()),
        new Claim(AuthorizationConstants.GroupPermissionsClaimType,
            new GroupPermissionsClaim { GroupId = ScopeId, Permissions = [GroupPermission.EditGroup] }.ToString())
    ], "Test"));
}
