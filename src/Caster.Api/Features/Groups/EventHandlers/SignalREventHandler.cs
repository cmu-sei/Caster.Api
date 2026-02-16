// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Caster.Api.Data;
using Crucible.Common.EntityEvents.Events;
using Caster.Api.Hubs;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.Groups.EventHandlers;

public class GroupMembershipCreatedSignalRHandler(CasterContext _db, IMapper _mapper, IHubContext<ProjectHub> _projectHub) :
    GroupMembershipBaseSignalRHandler(_db, _mapper, _projectHub),
    INotificationHandler<EntityCreated<Domain.Models.GroupMembership>>
{
    public async Task Handle(EntityCreated<Domain.Models.GroupMembership> notification, CancellationToken cancellationToken)
    {
        await base.Handle(notification.Entity, ProjectHubMethods.GroupMembershipCreated, null, cancellationToken);
    }
}

public class GroupMembershipUpdatedSignalRHandler(CasterContext _db, IMapper _mapper, IHubContext<ProjectHub> _projectHub) :
    GroupMembershipBaseSignalRHandler(_db, _mapper, _projectHub),
    INotificationHandler<EntityUpdated<Domain.Models.GroupMembership>>
{
    public async Task Handle(EntityUpdated<Domain.Models.GroupMembership> notification, CancellationToken cancellationToken)
    {
        await base.Handle(notification.Entity, ProjectHubMethods.GroupMembershipUpdated, notification.ModifiedProperties, cancellationToken);
    }
}

public class GroupMembershipDeletedSignalRHandler(IHubContext<ProjectHub> projectHub) :
    INotificationHandler<EntityDeleted<Domain.Models.GroupMembership>>
{
    public async Task Handle(EntityDeleted<Domain.Models.GroupMembership> notification, CancellationToken cancellationToken)
    {
        await projectHub.Clients.Group(notification.Entity.GroupId.ToString()).SendAsync(ProjectHubMethods.GroupMembershipDeleted, notification.Entity.Id);
    }
}

public class GroupMembershipBaseSignalRHandler(CasterContext db, IMapper mapper, IHubContext<ProjectHub> projectHub)
{
    protected async Task Handle(Domain.Models.GroupMembership entity, string method, string[] modifiedProperties, CancellationToken cancellationToken)
    {
        var groupMembership = await db.GroupMemberships
            .Where(x => x.Id == entity.Id)
            .ProjectTo<GroupMembership>(mapper.ConfigurationProvider)
            .FirstOrDefaultAsync();

        await projectHub.Clients.Group(entity.GroupId.ToString()).SendAsync(method, groupMembership, modifiedProperties, cancellationToken);
    }
}
