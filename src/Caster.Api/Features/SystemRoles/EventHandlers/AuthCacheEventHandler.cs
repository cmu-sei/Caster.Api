// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Events;
using Caster.Api.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Caster.Api.Features.Projects.EventHandlers;

public class SystemRoleBaseAuthCacheHandler(IMemoryCache cache, CasterContext dbContext)
{
    protected async Task UpdateCache(Domain.Models.SystemRole systemRole)
    {
        var userIds = await dbContext.Users
                    .Where(x => x.RoleId == systemRole.Id)
                    .Select(x => x.Id)
                    .ToListAsync();

        foreach (var userId in userIds)
        {
            cache.Remove(userId);
        }
    }
}

public class SystemRoleUpdatedAuthCacheHandler(IMemoryCache memoryCache, CasterContext db) : SystemRoleBaseAuthCacheHandler(memoryCache, db),
    INotificationHandler<EntityUpdated<Domain.Models.SystemRole>>
{
    public async Task Handle(EntityUpdated<Domain.Models.SystemRole> notification, CancellationToken cancellationToken)
    {
        if (notification.ModifiedProperties.Any(x =>
            x == nameof(SystemRole.Permissions) ||
            x == nameof(SystemRole.AllPermissions)))
        {
            await UpdateCache(notification.Entity);
        }
    }
}

public class SystemRoleDeletedAuthCacheHandler(IMemoryCache memoryCache, CasterContext db) : SystemRoleBaseAuthCacheHandler(memoryCache, db),
    INotificationHandler<EntityDeleted<Domain.Models.SystemRole>>
{
    public async Task Handle(EntityDeleted<Domain.Models.SystemRole> notification, CancellationToken cancellationToken)
    {
        await UpdateCache(notification.Entity);
    }
}