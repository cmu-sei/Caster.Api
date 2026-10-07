// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Text.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Tests.Support;

namespace Caster.Api.Tests.Infrastructure.Authorization;

/// <summary>
/// Caster's three requirement handlers, run directly over principals the claims transformer would produce
/// and over shapes it cannot: any one of the required permissions grants, and a scoped requirement only
/// matches a claim for its own project or group.
/// </summary>
public class PermissionHandlerTests
{
    [Fact]
    public async Task SystemPermissionsHandler_succeeds_when_the_caller_holds_any_one_of_the_permissions()
    {
        var user = new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ViewWorkspaces).Build();

        var context = await AuthorizationHarness.HandleAsync(
            new SystemPermissionsHandler(), new SystemPermissionRequirement([SystemPermission.ViewProjects, SystemPermission.ViewWorkspaces]), user);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task SystemPermissionsHandler_does_not_succeed_for_a_permission_name_that_differs_in_case()
    {
        var user = new ClaimsPrincipalBuilder().WithRawSystemPermission("viewprojects").Build();

        var context = await AuthorizationHarness.HandleAsync(
            new SystemPermissionsHandler(), new SystemPermissionRequirement([SystemPermission.ViewProjects]), user);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task ProjectPermissionsHandler_succeeds_for_the_permission_on_the_requirements_project()
    {
        var projectId = Guid.NewGuid();
        var user = new ClaimsPrincipalBuilder().WithProject(projectId, ProjectPermission.EditProject).Build();

        var context = await AuthorizationHarness.HandleAsync(
            new ProjectPermissionsHandler(), new ProjectPermissionRequirement([ProjectPermission.EditProject], projectId), user);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task ProjectPermissionsHandler_fails_for_the_permission_on_another_project()
    {
        var user = new ClaimsPrincipalBuilder().WithProject(Guid.NewGuid(), ProjectPermission.EditProject).Build();

        var context = await AuthorizationHarness.HandleAsync(
            new ProjectPermissionsHandler(), new ProjectPermissionRequirement([ProjectPermission.EditProject], Guid.NewGuid()), user);

        Assert.True(context.HasFailed);
    }

    /// <summary>A resource whose project could not be resolved never grants through a membership.</summary>
    [Fact]
    public async Task ProjectPermissionsHandler_fails_when_the_requirement_names_no_project()
    {
        var user = new ClaimsPrincipalBuilder().WithProject(Guid.NewGuid(), ProjectPermission.ViewProject).Build();

        var context = await AuthorizationHarness.HandleAsync(
            new ProjectPermissionsHandler(), new ProjectPermissionRequirement([ProjectPermission.ViewProject], null), user);

        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task ProjectPermissionsHandler_neither_succeeds_nor_fails_for_another_permission_on_the_project()
    {
        var projectId = Guid.NewGuid();
        var user = new ClaimsPrincipalBuilder().WithProject(projectId, ProjectPermission.ViewProject).Build();

        var context = await AuthorizationHarness.HandleAsync(
            new ProjectPermissionsHandler(), new ProjectPermissionRequirement([ProjectPermission.ManageProject], projectId), user);

        Assert.False(context.HasSucceeded);
        Assert.False(context.HasFailed);
    }

    [Fact]
    public async Task ProjectPermissionsHandler_throws_on_a_claim_that_is_not_json()
    {
        var user = new ClaimsPrincipalBuilder().WithRawProjectPermission("not json").Build();

        await Assert.ThrowsAnyAsync<JsonException>(() => AuthorizationHarness.HandleAsync(
            new ProjectPermissionsHandler(), new ProjectPermissionRequirement([ProjectPermission.ViewProject], Guid.NewGuid()), user));
    }

    [Fact]
    public async Task GroupPermissionsHandler_succeeds_for_ManageMembership_on_the_requirements_group()
    {
        var groupId = Guid.NewGuid();
        var user = new ClaimsPrincipalBuilder().WithGroup(groupId, GroupPermission.ManageMembership).Build();

        var context = await AuthorizationHarness.HandleAsync(
            new GroupPermissionsHandler(), new GroupPermissionRequirement([GroupPermission.ManageMembership], groupId), user);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task GroupPermissionsHandler_fails_for_ManageMembership_on_another_group()
    {
        var user = new ClaimsPrincipalBuilder().WithGroup(Guid.NewGuid(), GroupPermission.ManageMembership).Build();

        var context = await AuthorizationHarness.HandleAsync(
            new GroupPermissionsHandler(), new GroupPermissionRequirement([GroupPermission.ManageMembership], Guid.NewGuid()), user);

        Assert.True(context.HasFailed);
    }
}
