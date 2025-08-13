// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Caster.Api.Domain.Services;

namespace Caster.Api.Features.Workspaces.EventHandlers;

public class WorkspaceCreatedTelemetryHandler(CasterContext _db, TelemetryService telemetryService) :
    WorkspaceBaseTelemetryHandler(_db, telemetryService),
    INotificationHandler<EntityCreated<Domain.Models.Workspace>>
{
    public async Task Handle(EntityCreated<Domain.Models.Workspace> notification, CancellationToken cancellationToken)
    {
        await base.Handle(notification.Entity, cancellationToken);
    }
}

public class WorkspaceDeletedTelemetryHandler(CasterContext _db, TelemetryService telemetryService) :
    WorkspaceBaseTelemetryHandler(_db, telemetryService),
    INotificationHandler<EntityDeleted<Domain.Models.Workspace>>
{
    public async Task Handle(EntityDeleted<Domain.Models.Workspace> notification, CancellationToken cancellationToken)
    {
        await base.Handle(notification.Entity, cancellationToken);
    }
}

public class WorkspaceBaseTelemetryHandler(CasterContext _db, TelemetryService telemetryService)
{
    protected async Task Handle(Domain.Models.Workspace entity, CancellationToken cancellationToken)
    {
                var directoryMetrics = await _db.Directories.Select(d => new
                {
                    Directory = d.Name,
                    DirectoryId = d.Id,
                    Project = d.Project.Name,
                    ProjectId = d.ProjectId,
                    Count = d.Workspaces.Count
                }).SingleOrDefaultAsync(m => m.DirectoryId == entity.DirectoryId);
                telemetryService.Workspaces.Record(directoryMetrics.Count,
                    new KeyValuePair<string, object>("project", directoryMetrics.Project),
                    new KeyValuePair<string, object>("project_id", directoryMetrics.ProjectId),
                    new KeyValuePair<string, object>("directory", directoryMetrics.Directory),
                    new KeyValuePair<string, object>("directory_id", directoryMetrics.DirectoryId)
                );
                var projectMetrics = await _db.Projects.Select(p => new
                {
                    Project = p.Name,
                    ProjectId = p.Id,
                    Count = p.Directories.Select(d => d.Workspaces.Count).Sum()
                }).SingleOrDefaultAsync(m => m.ProjectId == entity.Directory.ProjectId);
                telemetryService.Workspaces.Record(projectMetrics.Count,
                    new KeyValuePair<string, object>("project", projectMetrics.Project),
                    new KeyValuePair<string, object>("project_id", projectMetrics.ProjectId)
                );    }
}
