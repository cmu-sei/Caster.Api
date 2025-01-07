// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Events;
using MediatR;
using Microsoft.Extensions.Caching.Memory;

namespace Caster.Api.Features.Projects.EventHandlers;

public class UserUpdatedAuthCacheHandler(IMemoryCache cache) :
    INotificationHandler<EntityUpdated<Domain.Models.User>>
{
    public Task Handle(EntityUpdated<Domain.Models.User> notification, CancellationToken cancellationToken)
    {
        if (notification.ModifiedProperties.Any(x => x == nameof(Domain.Models.User.RoleId)))
        {
            cache.Remove(notification.Entity.Id);
        }

        return Task.CompletedTask;
    }
}

public class UserDeletedAuthCacheHandler(IMemoryCache cache) :
    INotificationHandler<EntityDeleted<Domain.Models.User>>
{
    public Task Handle(EntityDeleted<Domain.Models.User> notification, CancellationToken cancellationToken)
    {
        cache.Remove(notification.Entity.Id);
        return Task.CompletedTask;
    }
}