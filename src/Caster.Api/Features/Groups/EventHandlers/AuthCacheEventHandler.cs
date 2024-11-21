// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Events;
using MediatR;
using Microsoft.Extensions.Caching.Memory;

namespace Caster.Api.Features.Groups.EventHandlers;

public class GroupMembershipAuthCacheHandler(IMemoryCache cache) :
    INotificationHandler<IEntityNotification<Domain.Models.GroupMembership>>
{
    public Task Handle(IEntityNotification<Domain.Models.GroupMembership> notification, CancellationToken cancellationToken)
    {
        cache.Remove(notification.Entity.UserId);
        return Task.CompletedTask;
    }
}