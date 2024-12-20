// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Caster.Api.Features.Projects.EventHandlers;

public class ProjectMembershipBaseAuthCacheHandler(IMemoryCache cache, CasterContext dbContext)
{
    protected async Task UpdateCache(Domain.Models.ProjectMembership membership)
    {
        if (membership.UserId.HasValue)
        {
            cache.Remove(membership.UserId);
        }

        if (membership.GroupId.HasValue)
        {
            var userIds = await dbContext.GroupMemberships
                .Where(x => x.GroupId == membership.GroupId.Value)
                .Select(x => x.UserId)
                .ToListAsync();

            foreach (var userId in userIds)
            {
                cache.Remove(userId);
            }
        }
    }
}

public class ProjectMembershipCreatedAuthCacheHandler(IMemoryCache memoryCache, CasterContext db) : ProjectMembershipBaseAuthCacheHandler(memoryCache, db),
    INotificationHandler<EntityCreated<Domain.Models.ProjectMembership>>
{
    public async Task Handle(EntityCreated<Domain.Models.ProjectMembership> notification, CancellationToken cancellationToken)
    {
        await UpdateCache(notification.Entity);
    }
}

public class ProjectMembershipUpdatedAuthCacheHandler(IMemoryCache memoryCache, CasterContext db) : ProjectMembershipBaseAuthCacheHandler(memoryCache, db),
    INotificationHandler<EntityUpdated<Domain.Models.ProjectMembership>>
{
    public async Task Handle(EntityUpdated<Domain.Models.ProjectMembership> notification, CancellationToken cancellationToken)
    {
        await UpdateCache(notification.Entity);
    }
}

public class ProjectMembershipDeletedAuthCacheHandler(IMemoryCache memoryCache, CasterContext db) : ProjectMembershipBaseAuthCacheHandler(memoryCache, db),
    INotificationHandler<EntityDeleted<Domain.Models.ProjectMembership>>
{
    public async Task Handle(EntityDeleted<Domain.Models.ProjectMembership> notification, CancellationToken cancellationToken)
    {
        await UpdateCache(notification.Entity);
    }
}