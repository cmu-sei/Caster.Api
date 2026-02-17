// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Caster.Api.Data;
using Crucible.Common.EntityEvents.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Caster.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Caster.Api.Features.SystemRoles.EventHandlers;

public class RoleCreatedSignalRHandler(CasterContext _db, IMapper _mapper, IHubContext<ProjectHub> _projectHub) :
    RoleBaseSignalRHandler(_db, _mapper, _projectHub),
    INotificationHandler<EntityCreated<Domain.Models.SystemRole>>
{

    public async Task Handle(EntityCreated<Domain.Models.SystemRole> notification, CancellationToken cancellationToken)
    {
        await base.Handle(notification.Entity, "RoleCreated", null, cancellationToken);
    }
}

public class RoleUpdatedSignalRHandler(CasterContext _db, IMapper _mapper, IHubContext<ProjectHub> _projectHub) :
    RoleBaseSignalRHandler(_db, _mapper, _projectHub),
    INotificationHandler<EntityUpdated<Domain.Models.SystemRole>>
{

    public async Task Handle(EntityUpdated<Domain.Models.SystemRole> notification, CancellationToken cancellationToken)
    {
        await base.Handle(notification.Entity, "RoleUpdated", notification.ModifiedProperties, cancellationToken);
    }
}

public class RoleDeletedSignalRHandler(IHubContext<ProjectHub> _projectHub) :
    INotificationHandler<EntityDeleted<Domain.Models.SystemRole>>
{

    public async Task Handle(EntityDeleted<Domain.Models.SystemRole> notification, CancellationToken cancellationToken)
    {
        await _projectHub.Clients.Groups(nameof(HubGroups.RolesAdmin)).SendAsync("RoleDeleted", notification.Entity.Id, cancellationToken);
    }
}

public class RoleBaseSignalRHandler(CasterContext _db, IMapper _mapper, IHubContext<ProjectHub> _projectHub)
{
    protected async Task Handle(Domain.Models.SystemRole entity, string method, string[] modifiedProperties, CancellationToken cancellationToken)
    {
        var role = await _db.SystemRoles
            .Where(r => r.Id == entity.Id)
            .ProjectTo<SystemRole>(_mapper.ConfigurationProvider)
            .FirstOrDefaultAsync();

        await _projectHub.Clients.Groups(nameof(HubGroups.RolesAdmin)).SendAsync(method, role, modifiedProperties, cancellationToken);
    }
}