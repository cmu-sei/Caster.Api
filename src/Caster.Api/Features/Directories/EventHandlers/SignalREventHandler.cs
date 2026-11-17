// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Crucible.Common.EntityEvents.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Caster.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Caster.Api.Features.Directories.EventHandlers;

public class DirectoryCreatedSignalRHandler(CasterContext _db, IHubContext<ProjectHub> _projectHub) :
    DirectoryBaseSignalRHandler(_db, _projectHub),
    INotificationHandler<EntityCreated<Domain.Models.Directory>>
{
    public async Task Handle(EntityCreated<Domain.Models.Directory> notification, CancellationToken cancellationToken)
    {
        await base.Handle(notification.Entity, "DirectoryCreated", null, cancellationToken);
    }
}

public class DirectoryUpdatedSignalRHandler(CasterContext _db, IHubContext<ProjectHub> _projectHub) :
    DirectoryBaseSignalRHandler(_db, _projectHub),
    INotificationHandler<EntityUpdated<Domain.Models.Directory>>
{
    public async Task Handle(EntityUpdated<Domain.Models.Directory> notification, CancellationToken cancellationToken)
    {
        await base.Handle(notification.Entity, "DirectoryUpdated", notification.ModifiedProperties, cancellationToken);
    }
}

public class DirectoryDeletedSignalRHandler(CasterContext _db, IHubContext<ProjectHub> _projectHub) :
    DirectoryBaseSignalRHandler(_db, _projectHub),
    INotificationHandler<EntityDeleted<Domain.Models.Directory>>
{
    public async Task Handle(EntityDeleted<Domain.Models.Directory> notification, CancellationToken cancellationToken)
    {
        await _projectHub.Clients.Group(notification.Entity.ProjectId.ToString()).SendAsync("DirectoryDeleted", notification.Entity.Id);
    }
}

public class DirectoryBaseSignalRHandler(CasterContext _db, IHubContext<ProjectHub> _projectHub)
{
    protected async Task Handle(Domain.Models.Directory entity, string method, string[] modifiedProperties, CancellationToken cancellationToken)
    {
        var directory = await _db.Directories
            .Where(d => d.Id == entity.Id)
            .ProjectToDirectory()
            .FirstOrDefaultAsync();

        await _projectHub.Clients.Group(directory.ProjectId.ToString()).SendAsync(method, directory, modifiedProperties, cancellationToken);
    }
}
