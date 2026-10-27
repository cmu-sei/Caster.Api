// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Tests.Support;
using Microsoft.AspNetCore.Authorization;

namespace Caster.Api.Tests.Infrastructure.Authorization;

/// <summary>
/// The three requirement handlers over a null, an empty and a non-empty permission list: only a non-empty
/// list with a matching permission, for the requirement's own project or group, succeeds. Each row names the
/// requirement kind (<c>System</c>, <c>Project</c>, <c>Group</c>), so a failure says which handler it was.
/// </summary>
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
        var context = await AuthorizationHarness.HandleAsync(Handler(kind), Unsatisfiable(kind, nullRequirements, ScopeId), Principal());

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
        var context = await AuthorizationHarness.HandleAsync(Handler(kind), TwoPermissions(kind), PrincipalHolding(hasPermission));

        Assert.Equal(hasPermission, context.HasSucceeded);
    }

    [Theory]
    [InlineData("Project")]
    [InlineData("Group")]
    public async Task Matching_permissions_for_a_different_scope_do_not_succeed(string kind)
    {
        var context = await AuthorizationHarness.HandleAsync(Handler(kind), HeldPermission(kind, Guid.NewGuid()), Principal());

        Assert.False(context.HasSucceeded);
    }

    private static IAuthorizationHandler Handler(string kind) => kind switch
    {
        "System" => new SystemPermissionsHandler(),
        "Project" => new ProjectPermissionsHandler(),
        _ => new GroupPermissionsHandler()
    };

    /// <summary>A requirement of <paramref name="kind"/> over a null or an empty permission list.</summary>
    private static IAuthorizationRequirement Unsatisfiable(string kind, bool nullRequirements, Guid scopeId) => kind switch
    {
        "System" => new SystemPermissionRequirement(nullRequirements ? null : []),
        "Project" => new ProjectPermissionRequirement(nullRequirements ? null : [], scopeId),
        _ => new GroupPermissionRequirement(nullRequirements ? null : [], scopeId)
    };

    /// <summary>A requirement of <paramref name="kind"/> on <see cref="ScopeId"/> that one of two permissions meets.</summary>
    private static IAuthorizationRequirement TwoPermissions(string kind) => kind switch
    {
        "System" => new SystemPermissionRequirement([SystemPermission.ViewProjects, SystemPermission.EditProjects]),
        "Project" => new ProjectPermissionRequirement([ProjectPermission.ViewProject, ProjectPermission.EditProject], ScopeId),
        _ => new GroupPermissionRequirement([GroupPermission.ManageMembership, GroupPermission.EditGroup], ScopeId)
    };

    /// <summary>A requirement of <paramref name="kind"/> for exactly the permission <see cref="Principal"/> holds, on <paramref name="scopeId"/>.</summary>
    private static IAuthorizationRequirement HeldPermission(string kind, Guid scopeId) => kind == "Project"
        ? new ProjectPermissionRequirement([ProjectPermission.EditProject], scopeId)
        : new GroupPermissionRequirement([GroupPermission.EditGroup], scopeId);

    private static ClaimsPrincipal PrincipalHolding(bool hasPermission) =>
        hasPermission ? Principal() : ClaimsPrincipalBuilder.Anonymous();

    /// <summary>EditProjects, EditProject on <see cref="ScopeId"/> and EditGroup on <see cref="ScopeId"/>.</summary>
    private static ClaimsPrincipal Principal() => new ClaimsPrincipalBuilder()
        .WithSystemPermissions(SystemPermission.EditProjects)
        .WithProject(ScopeId, ProjectPermission.EditProject)
        .WithGroup(ScopeId, GroupPermission.EditGroup)
        .Build();
}
