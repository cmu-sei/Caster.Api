// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Caster.Api.Tests.Domain.Services;

/// <summary>
/// <see cref="UserClaimsService"/>, the claims transformer's source: what it derives from the user's rows and
/// from the token's roles and groups, its per-user cache, and the user row it writes on first sight. The
/// hosted application runs with caching and the IdP paths off (<c>TestConfiguration</c>), so they are driven
/// here directly.
/// </summary>
public class UserClaimsServiceTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task A_role_named_in_the_tokens_realm_access_grants_its_permissions()
    {
        var user = TestData.User();
        await Seed(user);
        var token = Token(user.Id, new Claim("realm_access", """{"roles":["Content Developer"]}""", JsonClaimValueTypes.Json));

        var principal = await Service(new() { UseRolesFromIdP = true, RolesClaimPath = "realm_access.roles" }).AddUserClaims(token, update: false);

        Assert.Equal(["CreateProjects"], Permissions(principal));
    }

    [Fact]
    public async Task Roles_in_the_token_are_ignored_when_UseRolesFromIdP_is_off()
    {
        var user = TestData.User();
        await Seed(user);
        var token = Token(user.Id, new Claim("realm_access", """{"roles":["Content Developer"]}""", JsonClaimValueTypes.Json));

        var principal = await Service(new() { RolesClaimPath = "realm_access.roles" }).AddUserClaims(token, update: false);

        Assert.Empty(Permissions(principal));
    }

    [Fact]
    public async Task A_group_named_in_the_token_grants_the_groups_project_membership()
    {
        var user = TestData.User();
        var group = TestData.Group("Token Group");
        var project = TestData.Project();
        await Seed(user, group, project, TestData.ProjectMembership(project.Id, TestData.ProjectRoles.Observer, groupId: group.Id));
        var token = Token(user.Id, new Claim("groups", "Token Group"));

        var principal = await Service(new() { UseGroupsFromIdP = true, GroupsClaimPath = "groups" }).AddUserClaims(token, update: false);

        var claim = Assert.Single(ProjectClaims(principal));
        Assert.Equal(project.Id, claim.ProjectId);
        Assert.Equal([ProjectPermission.ViewProject], claim.Permissions);
    }

    [Fact]
    public async Task Memberships_of_one_project_combine_their_roles_into_one_claim()
    {
        var user = TestData.User();
        var group = TestData.Group();
        var project = TestData.Project();
        var editRole = TestData.ProjectRole(permissions: [ProjectPermission.EditProject]);
        await Seed(user, group, project, editRole,
            TestData.GroupMembership(group.Id, user.Id),
            TestData.ProjectMembership(project.Id, TestData.ProjectRoles.Observer, userId: user.Id),
            TestData.ProjectMembership(project.Id, editRole.Id, groupId: group.Id));

        var principal = await Service(new()).AddUserClaims(Token(user.Id), update: false);

        var claim = Assert.Single(ProjectClaims(principal));
        Assert.Equal(new[] { ProjectPermission.ViewProject, ProjectPermission.EditProject }.Order(), claim.Permissions.Order());
    }

    [Fact]
    public async Task With_caching_on_a_second_call_answers_from_the_cache_until_the_claims_are_refreshed()
    {
        var user = TestData.User();
        await Seed(user);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var options = new ClaimsTransformationOptions { EnableCaching = true, CacheExpirationSeconds = 60 };
        await Service(options, cache).AddUserClaims(Token(user.Id), update: false);
        await GrantRole(user.Id, TestData.Roles.ContentDeveloper);

        var cached = await Service(options, cache).AddUserClaims(Token(user.Id), update: false);
        var refreshed = await Service(options, cache).RefreshClaims(user.Id);

        Assert.Empty(Permissions(cached));
        Assert.Equal(["CreateProjects"], Permissions(refreshed));
    }

    [Fact]
    public async Task A_first_call_with_update_creates_the_user_from_the_name_claim()
    {
        var id = Guid.NewGuid();

        await Service(new()).AddUserClaims(Token(id, new Claim("name", "First Sight")), update: true);

        await using var context = NewContext();
        Assert.Equal("First Sight", (await context.Users.SingleAsync(x => x.Id == id, Ct)).Name);
    }

    [Fact]
    public async Task An_unknown_user_without_update_gets_no_permission_claims()
    {
        var principal = await Service(new()).AddUserClaims(Token(Guid.NewGuid()), update: false);

        Assert.Empty(Permissions(principal));
    }

    /// <summary>The cache a service gets when the test does not hand it one of its own.</summary>
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    private UserClaimsService Service(ClaimsTransformationOptions options, IMemoryCache cache = null) =>
        new(Db, cache ?? _cache, options);

    public override async ValueTask DisposeAsync()
    {
        _cache.Dispose();
        await base.DisposeAsync();
    }

    private async Task GrantRole(Guid userId, Guid roleId)
    {
        await using var context = NewContext();
        var user = await context.Users.SingleAsync(x => x.Id == userId, Ct);
        user.RoleId = roleId;
        await context.SaveChangesAsync(Ct);
    }

    /// <summary>The identity a validated token carries before the transformer runs: a subject, and whatever else is given.</summary>
    private static ClaimsPrincipal Token(Guid userId, params Claim[] claims) =>
        new(new ClaimsIdentity([new Claim("sub", userId.ToString()), .. claims], "Test"));

    private static string[] Permissions(ClaimsPrincipal principal) =>
        [.. principal.Claims.Where(x => x.Type == AuthorizationConstants.PermissionsClaimType).Select(x => x.Value).Order()];

    private static ProjectPermissionsClaim[] ProjectClaims(ClaimsPrincipal principal) =>
        [.. principal.Claims
            .Where(x => x.Type == AuthorizationConstants.ProjectPermissionsClaimType)
            .Select(x => ProjectPermissionsClaim.FromString(x.Value))];
}
