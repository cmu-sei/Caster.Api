// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Tests.Support;

namespace Caster.Api.Tests.Features.GroupPermissions;

/// <summary><c>GET api/permissions/group/mine</c>: the caller's group permission claims, one per group they manage.</summary>
public class GroupPermissionRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    [Fact]
    public async Task GetMine_returns_ManageMembership_on_the_group_the_caller_manages()
    {
        var actor = await Actor().OnNewGroup(GroupMembershipRole.Manager).SeedAsync();

        var response = await Client(actor).GetAsync("api/permissions/group/mine", Ct);

        var claim = Assert.Single(await ReadAsync<GroupPermissionsClaim[]>(response));
        Assert.Equal(actor.GroupMembership.GroupId, claim.GroupId);
        Assert.Equal([GroupPermission.ManageMembership], claim.Permissions);
    }

    [Fact]
    public async Task GetMine_returns_nothing_to_a_plain_member_of_a_group()
    {
        var actor = await Actor().OnNewGroup(GroupMembershipRole.Member).SeedAsync();

        var response = await Client(actor).GetAsync("api/permissions/group/mine", Ct);

        Assert.Empty(await ReadAsync<GroupPermissionsClaim[]>(response));
    }
}
