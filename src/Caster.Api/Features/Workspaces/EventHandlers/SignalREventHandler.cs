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

namespace Caster.Api.Features.Workspaces.EventHandlers;

public class WorkspaceCreatedSignalRHandler(CasterContext _db, IMapper _mapper, IHubContext<ProjectHub> _projectHub) :
    WorkspaceBaseSignalRHandler(_db, _mapper, _projectHub),
    INotificationHandler<EntityCreated<Domain.Models.Workspace>>
{
    public async Task Handle(EntityCreated<Domain.Models.Workspace> notification, CancellationToken cancellationToken)
    {
        await base.Handle(notification.Entity, "WorkspaceCreated", [], cancellationToken);
    }
}

public class WorkspaceUpdatedSignalRHandler(CasterContext _db, IMapper _mapper, IHubContext<ProjectHub> _projectHub) :
    WorkspaceBaseSignalRHandler(_db, _mapper, _projectHub),
    INotificationHandler<EntityUpdated<Domain.Models.Workspace>>
{
    public async Task Handle(EntityUpdated<Domain.Models.Workspace> notification, CancellationToken cancellationToken)
    {
        await base.Handle(notification.Entity, "WorkspaceUpdated", notification.ModifiedProperties, cancellationToken);
    }
}

public class WorkspaceDeletedSignalRHandler(CasterContext _db, IHubContext<ProjectHub> _projectHub) :
    INotificationHandler<EntityDeleted<Domain.Models.Workspace>>
{
    public async Task Handle(EntityDeleted<Domain.Models.Workspace> notification, CancellationToken cancellationToken)
    {
        var projectId = await _db.Directories
                .Where(d => d.Id == notification.Entity.DirectoryId)
                .Select(d => d.ProjectId)
                .FirstOrDefaultAsync();

        await _projectHub.Clients.Group(projectId.ToString()).SendAsync("WorkspaceDeleted", notification.Entity.Id, cancellationToken);
    }
}

public class WorkspaceBaseSignalRHandler(CasterContext _db, IMapper _mapper, IHubContext<ProjectHub> _projectHub)
{
    protected async Task Handle(Domain.Models.Workspace entity, string method, string[] modifiedProperties, CancellationToken cancellationToken)
    {
        var workspace = await _db.Workspaces
           .Where(d => d.Id == entity.Id)
           .ProjectTo<Workspace>(_mapper.ConfigurationProvider)
           .FirstOrDefaultAsync();

        var projectId = await _db.Directories
            .Where(d => d.Id == workspace.DirectoryId)
            .Select(d => d.ProjectId)
            .FirstOrDefaultAsync();

        await _projectHub.Clients.Group(projectId.ToString()).SendAsync(method, workspace, modifiedProperties, cancellationToken);
    }
}
