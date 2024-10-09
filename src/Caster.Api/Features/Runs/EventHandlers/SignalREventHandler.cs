// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Caster.Api.Data;
using Caster.Api.Domain.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Caster.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Caster.Api.Features.Runs.EventHandlers;

public class RunCreatedSignalRHandler(CasterContext _db, IMapper _mapper, IHubContext<ProjectHub> _projectHub) :
    RunBaseSignalRHandler(_db, _mapper, _projectHub),
    INotificationHandler<EntityCreated<Domain.Models.Run>>
{

    public async Task Handle(EntityCreated<Domain.Models.Run> notification, CancellationToken cancellationToken)
    {
        await base.Handle(notification.Entity, "RunCreated", null, cancellationToken);
    }
}

public class RunUpdatedSignalRHandler(CasterContext _db, IMapper _mapper, IHubContext<ProjectHub> _projectHub) :
    RunBaseSignalRHandler(_db, _mapper, _projectHub),
    INotificationHandler<EntityUpdated<Domain.Models.Run>>
{

    public async Task Handle(EntityUpdated<Domain.Models.Run> notification, CancellationToken cancellationToken)
    {
        await base.Handle(notification.Entity, "RunUpdated", notification.ModifiedProperties, cancellationToken);
    }
}

public class RunDeletedSignalRHandler(IHubContext<ProjectHub> _projectHub) :
    INotificationHandler<EntityDeleted<Domain.Models.Run>>
{

    public async Task Handle(EntityDeleted<Domain.Models.Run> notification, CancellationToken cancellationToken)
    {
        await _projectHub.Clients.Groups(notification.Entity.WorkspaceId.ToString(), nameof(HubGroups.WorkspacesAdmin)).SendAsync("RunDeleted", notification.Entity.Id, cancellationToken);
    }
}

public class RunBaseSignalRHandler(CasterContext _db, IMapper _mapper, IHubContext<ProjectHub> _projectHub)
{
    protected async Task Handle(Domain.Models.Run entity, string method, string[] modifiedProperties, CancellationToken cancellationToken)
    {
        var run = await _db.Runs
            .Where(r => r.Id == entity.Id)
            .ProjectTo<Run>(_mapper.ConfigurationProvider)
            .FirstOrDefaultAsync();

        await _projectHub.Clients.Groups(run.WorkspaceId.ToString(), nameof(HubGroups.WorkspacesAdmin)).SendAsync(method, run, modifiedProperties, cancellationToken);
    }
}