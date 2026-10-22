// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// Caster's claim shapes: one "Permission" claim per system permission, one "ProjectPermission" claim per
// project holding a serialized ProjectPermissionsClaim, one "GroupPermission" claim per managed group holding
// a serialized GroupPermissionsClaim (AuthorizationConstants, UserClaimsService.GetPermissionClaims).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Authorization;

namespace Caster.Api.Tests.Support;

/// <summary>
/// Builds the principal the authorization stack sees for a signed-in user, for tests of the authorization
/// stack itself. Never an HTTP test's caller: that is <see cref="TestActor"/>.
/// </summary>
public sealed class ClaimsPrincipalBuilder
{
    private readonly List<Claim> _claims = [];
    private Guid _userId = Guid.NewGuid();
    private string _name = "Test User";

    public Guid UserId => _userId;

    public ClaimsPrincipalBuilder WithUserId(Guid userId)
    {
        _userId = userId;
        return this;
    }

    public ClaimsPrincipalBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    /// <summary>Adds system permissions, which grant across every resource.</summary>
    public ClaimsPrincipalBuilder WithSystemPermissions(params SystemPermission[] permissions)
    {
        foreach (var permission in permissions)
        {
            _claims.Add(new Claim(AuthorizationConstants.PermissionsClaimType, permission.ToString()));
        }

        return this;
    }

    /// <summary>A raw system-permission value, for values that are not enum names.</summary>
    public ClaimsPrincipalBuilder WithRawSystemPermission(string value)
    {
        _claims.Add(new Claim(AuthorizationConstants.PermissionsClaimType, value));
        return this;
    }

    /// <summary>The permissions held on one project, as the claims service serializes them.</summary>
    public ClaimsPrincipalBuilder WithProject(Guid projectId, params ProjectPermission[] permissions)
    {
        var claim = new ProjectPermissionsClaim { ProjectId = projectId, Permissions = permissions };
        _claims.Add(new Claim(AuthorizationConstants.ProjectPermissionsClaimType, claim.ToString()));
        return this;
    }

    /// <summary>The permissions held on one group, as the claims service serializes them.</summary>
    public ClaimsPrincipalBuilder WithGroup(Guid groupId, params GroupPermission[] permissions)
    {
        var claim = new GroupPermissionsClaim { GroupId = groupId, Permissions = permissions };
        _claims.Add(new Claim(AuthorizationConstants.GroupPermissionsClaimType, claim.ToString()));
        return this;
    }

    /// <summary>A raw project-permission claim value, for shapes the claims service never writes.</summary>
    public ClaimsPrincipalBuilder WithRawProjectPermission(string value)
    {
        _claims.Add(new Claim(AuthorizationConstants.ProjectPermissionsClaimType, value));
        return this;
    }

    /// <summary>An arbitrary claim, for asserting that unrelated claim types are ignored.</summary>
    public ClaimsPrincipalBuilder WithClaim(string type, string value)
    {
        _claims.Add(new Claim(type, value));
        return this;
    }

    public ClaimsPrincipal Build()
    {
        var claims = new List<Claim>(_claims) { new("sub", _userId.ToString()), new("name", _name) };

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    /// <summary>An authenticated principal with no permissions, the baseline every check must reject.</summary>
    public static ClaimsPrincipal Anonymous() => new ClaimsPrincipalBuilder().Build();
}
