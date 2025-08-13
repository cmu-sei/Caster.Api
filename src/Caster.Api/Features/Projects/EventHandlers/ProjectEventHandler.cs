// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Caster.Api.Data;
using Caster.Api.Domain.Events;
using Caster.Api.Domain.Services;
using Caster.Api.Hubs;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.Projects.EventHandlers;

public class ProjectCreatedSignalRHandler(CasterContext _db, TelemetryService telemetryService) :
    ProjectBaseSignalRHandler(_db, telemetryService),
    INotificationHandler<EntityCreated<Domain.Models.Project>>
{
    public async Task Handle(EntityCreated<Domain.Models.Project> notification, CancellationToken cancellationToken)
    {
        await base.Handle(notification.Entity, cancellationToken);
    }
}

public class ProjectDeletedSignalRHandler(CasterContext _db, TelemetryService telemetryService) :
    ProjectBaseSignalRHandler(_db, telemetryService),
    INotificationHandler<EntityDeleted<Domain.Models.Project>>
{
    public async Task Handle(EntityDeleted<Domain.Models.Project> notification, CancellationToken cancellationToken)
    {
        await base.Handle(notification.Entity, cancellationToken);
    }
}

public class ProjectBaseSignalRHandler(CasterContext db, TelemetryService telemetryService)
{
    protected async Task Handle(Domain.Models.Project entity, CancellationToken cancellationToken)
    {
        var count = await db.Projects.LongCountAsync(cancellationToken);
        telemetryService.Projects.Record((int)count);

    }
}
