// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// Runs Caster's real UserClaimsService over what TestActorBuilder seeded, with caching and IdP roles and
// groups off, as TestConfiguration sets them for the hosted application.

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Caster.Api.Tests.Support;

/// <summary>
/// Tests for <see cref="TestActorBuilder"/>: an actor that holds more than asked turns an authorization test
/// into a formality.
/// </summary>
/// <remarks>
/// These run the real claims service over the seeded rows rather than making a request, because a status
/// code cannot tell which of several grants answered.
/// </remarks>
public class TestActorTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task WithAllSystemPermissions_grants_every_system_permission()
    {
        var actor = await Actor().WithAllSystemPermissions().SeedAsync();

        Assert.Equal(Enum.GetNames<SystemPermission>().Order(), Permissions(await ClaimsOf(actor)));
    }

    [Fact]
    public async Task WithSystemPermissions_grants_exactly_what_it_names()
    {
        var actor = await Actor()
            .WithSystemPermissions(SystemPermission.ViewProjects, SystemPermission.ManageHosts)
            .SeedAsync();

        Assert.Equal(["ManageHosts", "ViewProjects"], Permissions(await ClaimsOf(actor)));
    }

    [Fact]
    public async Task WithRole_grants_that_roles_permissions()
    {
        var actor = await Actor().WithRole(TestData.Roles.ContentDeveloper).SeedAsync();

        Assert.Equal(["CreateProjects"], Permissions(await ClaimsOf(actor)));
    }

    /// <summary>The baseline an authorization test starts from: authenticated and holding nothing.</summary>
    [Fact]
    public async Task An_actor_with_no_role_holds_nothing()
    {
        var actor = await Actor().SeedAsync();

        var principal = await ClaimsOf(actor);

        Assert.Empty(Permissions(principal));
        Assert.Empty(ProjectClaims(principal));
        Assert.Empty(GroupClaims(principal));
    }

    [Fact]
    public async Task The_user_row_carries_the_id_and_name()
    {
        var id = Guid.NewGuid();

        var actor = await Actor().WithId(id).WithName("Named").SeedAsync();

        Assert.Equal(id, actor.Id);
        await using var context = NewContext();
        Assert.Equal("Named", (await context.Users.SingleAsync(x => x.Id == id, Ct)).Name);
    }

    [Fact]
    public async Task OnProject_grants_exactly_what_it_names_on_that_project()
    {
        var project = await SeedProject();

        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var claim = Assert.Single(ProjectClaims(await ClaimsOf(actor)));
        Assert.Equal(project.Id, claim.ProjectId);
        Assert.Equal([ProjectPermission.ViewProject], claim.Permissions);
    }

    [Fact]
    public async Task OnProject_with_the_seeded_manager_role_grants_every_project_permission()
    {
        var project = await SeedProject();

        var actor = await Actor().OnProject(project, roleId: TestData.ProjectRoles.Manager).SeedAsync();

        var claim = Assert.Single(ProjectClaims(await ClaimsOf(actor)));
        Assert.Equal(Enum.GetValues<ProjectPermission>().Order(), claim.Permissions.Order());
    }

    /// <summary>
    /// The minted project is a different one, and the actor holds exactly the permissions passed there: the
    /// near miss "the right permission on another project".
    /// </summary>
    [Fact]
    public async Task OnNewProject_grants_exactly_what_it_names_on_a_project_of_its_own()
    {
        var project = await SeedProject();

        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var claim = Assert.Single(ProjectClaims(await ClaimsOf(actor)));
        Assert.NotEqual(project.Id, claim.ProjectId);
        Assert.Equal(actor.ProjectMembership.ProjectId, claim.ProjectId);
        Assert.Equal([ProjectPermission.EditProject], claim.Permissions);
    }

    [Fact]
    public async Task OnProjectThroughNewGroup_grants_the_groups_project_permissions_to_its_member()
    {
        var project = await SeedProject();

        var actor = await Actor().OnProjectThroughNewGroup(project, ProjectPermission.ManageProject).SeedAsync();

        var principal = await ClaimsOf(actor);
        var claim = Assert.Single(ProjectClaims(principal));
        Assert.Equal(project.Id, claim.ProjectId);
        Assert.Equal([ProjectPermission.ManageProject], claim.Permissions);
        Assert.NotNull(actor.ProjectMembership.GroupId);
        Assert.Empty(GroupClaims(principal));
    }

    [Fact]
    public async Task InGroup_as_a_manager_grants_ManageMembership_on_that_group()
    {
        var group = TestData.Group();
        await Seed(group);

        var actor = await Actor().InGroup(group, GroupMembershipRole.Manager).SeedAsync();

        var claim = Assert.Single(GroupClaims(await ClaimsOf(actor)));
        Assert.Equal(group.Id, claim.GroupId);
        Assert.Equal([GroupPermission.ManageMembership], claim.Permissions);
    }

    [Fact]
    public async Task InGroup_as_a_member_grants_nothing()
    {
        var group = TestData.Group();
        await Seed(group);

        var actor = await Actor().InGroup(group).SeedAsync();

        var principal = await ClaimsOf(actor);
        Assert.Empty(GroupClaims(principal));
        Assert.Empty(ProjectClaims(principal));
    }

    [Fact]
    public async Task OnNewGroup_grants_ManageMembership_on_a_group_of_its_own()
    {
        var group = TestData.Group();
        await Seed(group);

        var actor = await Actor().OnNewGroup().SeedAsync();

        var claim = Assert.Single(GroupClaims(await ClaimsOf(actor)));
        Assert.NotEqual(group.Id, claim.GroupId);
        Assert.Equal(actor.GroupMembership.GroupId, claim.GroupId);
    }

    [Fact]
    public void WithRole_after_WithSystemPermissions_throws()
    {
        var builder = Actor().WithSystemPermissions(SystemPermission.ViewProjects);

        Assert.Throws<InvalidOperationException>(() => builder.WithRole(TestData.Roles.Administrator));
    }

    [Fact]
    public void WithSystemPermissions_after_WithRole_throws()
    {
        var builder = Actor().WithRole(TestData.Roles.Administrator);

        Assert.Throws<InvalidOperationException>(() => builder.WithSystemPermissions(SystemPermission.ViewProjects));
    }

    /// <summary>A membership without a named role would silently take the seeded Member role's three permissions.</summary>
    [Fact]
    public void OnProject_with_neither_permissions_nor_a_role_throws()
    {
        Assert.Throws<InvalidOperationException>(() => Actor().OnProject(TestData.Project()));
    }

    [Fact]
    public void OnProject_with_both_permissions_and_a_role_throws()
    {
        Assert.Throws<InvalidOperationException>(() => Actor().OnProject(
            TestData.Project(), [ProjectPermission.ViewProject], TestData.ProjectRoles.Observer));
    }

    private TestActorBuilder Actor() => new(Db, Ct);

    private async Task<Project> SeedProject()
    {
        var project = TestData.Project();
        await Seed(project);

        return project;
    }

    /// <summary>The claims the real service derives from what the builder seeded. Caching is off.</summary>
    private async Task<ClaimsPrincipal> ClaimsOf(TestActor actor)
    {
        await using var context = NewContext();
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var service = new UserClaimsService(
            context,
            cache,
            new ClaimsTransformationOptions { EnableCaching = false, UseRolesFromIdP = false, UseGroupsFromIdP = false });

        return await service.GetClaimsPrincipal(actor.Id, setAsCurrent: true);
    }

    private static string[] Permissions(ClaimsPrincipal principal) =>
        [.. principal.Claims
            .Where(x => x.Type == AuthorizationConstants.PermissionsClaimType)
            .Select(x => x.Value)
            .Order()];

    private static ProjectPermissionsClaim[] ProjectClaims(ClaimsPrincipal principal) =>
        [.. principal.Claims
            .Where(x => x.Type == AuthorizationConstants.ProjectPermissionsClaimType)
            .Select(x => ProjectPermissionsClaim.FromString(x.Value))];

    private static GroupPermissionsClaim[] GroupClaims(ClaimsPrincipal principal) =>
        [.. principal.Claims
            .Where(x => x.Type == AuthorizationConstants.GroupPermissionsClaimType)
            .Select(x => GroupPermissionsClaim.FromString(x.Value))];
}
