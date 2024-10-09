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

namespace Caster.Api.Features.Files.EventHandlers;

public class FileCreatedSignalRHandler(CasterContext _db, IGetFileQuery _fileQuery, IHubContext<ProjectHub> _projectHub) :
    FileBaseSignalRHandler(_db, _fileQuery, _projectHub),
    INotificationHandler<EntityCreated<Domain.Models.File>>
{
    public async Task Handle(EntityCreated<Domain.Models.File> notification, CancellationToken cancellationToken)
    {
        await base.Handle(notification.Entity, "FileCreated", [], cancellationToken);
    }
}

public class FileUpdatedSignalRHandler(CasterContext _db, IGetFileQuery _fileQuery, IHubContext<ProjectHub> _projectHub) :
    FileBaseSignalRHandler(_db, _fileQuery, _projectHub),
    INotificationHandler<EntityUpdated<Domain.Models.File>>
{
    public async Task Handle(EntityUpdated<Domain.Models.File> notification, CancellationToken cancellationToken)
    {
        await base.Handle(notification.Entity, "FileUpdated", notification.ModifiedProperties, cancellationToken);
    }
}

public class FileDeletedSignalRHandler(CasterContext _db, IGetFileQuery _fileQuery, IHubContext<ProjectHub> _projectHub) :
    FileBaseSignalRHandler(_db, _fileQuery, _projectHub),
    INotificationHandler<EntityDeleted<Domain.Models.File>>
{
    public async Task Handle(EntityDeleted<Domain.Models.File> notification, CancellationToken cancellationToken)
    {
        var projectId = await _db.Directories
                .Where(d => d.Id == notification.Entity.DirectoryId)
                .Select(d => d.ProjectId)
                .FirstOrDefaultAsync();

        await _projectHub.Clients.Group(projectId.ToString()).SendAsync("FileDeleted", notification.Entity.Id, cancellationToken);
    }
}

public class FileBaseSignalRHandler(CasterContext _db, IGetFileQuery _fileQuery, IHubContext<ProjectHub> _projectHub)
{
    protected async Task Handle(Domain.Models.File entity, string method, string[] modifiedProperties, CancellationToken cancellationToken)
    {
        var file = await _fileQuery.ExecuteAsync(
            entity.Id,
            modifiedProperties.Contains(nameof(entity.Content)));

        var projectId = await _db.Directories
            .Where(d => d.Id == file.DirectoryId)
            .Select(d => d.ProjectId)
            .FirstOrDefaultAsync();

        await _projectHub.Clients.Group(projectId.ToString()).SendAsync(method, file, modifiedProperties, cancellationToken);
    }
}