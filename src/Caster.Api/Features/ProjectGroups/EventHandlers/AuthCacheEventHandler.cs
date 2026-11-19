// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Crucible.Common.EntityEvents.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Caster.Api.Features.ProjectGroups.EventHandlers;

/// <summary>
/// A user's project permissions are cached, and a group's memberships give permissions in every
/// project of the group. A change to who is in a group, or to which projects are, takes effect at
/// once, as a change to a project's own memberships does.
/// </summary>
public static class ProjectGroupAuthCache
{
    /// <summary>Forgets the cached permissions of everyone holding a membership of <paramref name="groupIds"/>.</summary>
    public static async Task ClearMembers(this IMemoryCache cache, CasterContext dbContext, IReadOnlyCollection<Guid> groupIds, CancellationToken cancellationToken)
    {
        if (groupIds.Count == 0)
            return;

        var memberships = await dbContext.ProjectGroupMemberships
            .Where(x => groupIds.Contains(x.ProjectGroupId))
            .Select(x => new { x.UserId, x.GroupId })
            .ToListAsync(cancellationToken);

        await cache.ClearMembers(dbContext,
            memberships.Where(x => x.UserId.HasValue).Select(x => x.UserId.Value),
            memberships.Where(x => x.GroupId.HasValue).Select(x => x.GroupId.Value),
            cancellationToken);
    }

    internal static async Task ClearMembers(this IMemoryCache cache, CasterContext dbContext, IEnumerable<Guid> userIds, IEnumerable<Guid> userGroupIds, CancellationToken cancellationToken)
    {
        var groupIds = userGroupIds.Distinct().ToList();
        var viaGroups = groupIds.Count == 0
            ? []
            : await dbContext.GroupMemberships
                .Where(x => groupIds.Contains(x.GroupId))
                .Select(x => x.UserId)
                .ToListAsync(cancellationToken);

        foreach (var userId in userIds.Concat(viaGroups).Distinct())
        {
            cache.Remove(userId);
        }
    }
}

public class ProjectGroupMembershipAuthCacheHandler(IMemoryCache cache, CasterContext dbContext) :
    INotificationHandler<EntityCreated<ProjectGroupMembership>>,
    INotificationHandler<EntityUpdated<ProjectGroupMembership>>,
    INotificationHandler<EntityDeleted<ProjectGroupMembership>>
{
    public Task Handle(EntityCreated<ProjectGroupMembership> notification, CancellationToken cancellationToken) =>
        Clear(notification.Entity, cancellationToken);

    public Task Handle(EntityUpdated<ProjectGroupMembership> notification, CancellationToken cancellationToken) =>
        Clear(notification.Entity, cancellationToken);

    public Task Handle(EntityDeleted<ProjectGroupMembership> notification, CancellationToken cancellationToken) =>
        Clear(notification.Entity, cancellationToken);

    private Task Clear(ProjectGroupMembership membership, CancellationToken cancellationToken) =>
        cache.ClearMembers(dbContext,
            membership.UserId.HasValue ? [membership.UserId.Value] : [],
            membership.GroupId.HasValue ? [membership.GroupId.Value] : [],
            cancellationToken);
}
