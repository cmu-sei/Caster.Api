// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Crucible.Common.EntityEvents.Events;
using Caster.Api.Hubs;
using Caster.Api.Infrastructure.Extensions;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;


namespace Caster.Api.Features.Designs.EventHandlers;

public class DesignCreatedSignalRHandler : DesignBaseSignalRHandler, INotificationHandler<EntityCreated<Domain.Models.Design>>
{
    public DesignCreatedSignalRHandler(
        IHubContext<ProjectHub> projectHub,
        CasterContext db) : base(projectHub, db) { }

    public async Task Handle(EntityCreated<Domain.Models.Design> notification, CancellationToken cancellationToken)
    {
        await base.HandleCreateOrUpdate(notification.Entity, ProjectHubMethods.DesignCreated, null, cancellationToken);
    }
}

public class DesignUpdatedSignalRHandler : DesignBaseSignalRHandler, INotificationHandler<EntityUpdated<Domain.Models.Design>>
{
    public DesignUpdatedSignalRHandler(
        IHubContext<ProjectHub> projectHub,
        CasterContext db) : base(projectHub, db) { }

    public async Task Handle(EntityUpdated<Domain.Models.Design> notification, CancellationToken cancellationToken)
    {
        await base.HandleCreateOrUpdate(
            notification.Entity,
            ProjectHubMethods.DesignUpdated,
            notification.ModifiedProperties,
            cancellationToken);
    }
}

public class DesignDeletedSignalRHandler : DesignBaseSignalRHandler, INotificationHandler<EntityDeleted<Domain.Models.Design>>
{
    public DesignDeletedSignalRHandler(
        IHubContext<ProjectHub> projectHub,
        CasterContext db) : base(projectHub, db) { }

    public async Task Handle(EntityDeleted<Domain.Models.Design> notification, CancellationToken cancellationToken)
    {
        var group = await base.GetGroup(notification.Entity, cancellationToken);
        await _projectHub.Clients.Group(group).SendAsync(ProjectHubMethods.DesignDeleted, notification.Entity.Id);
    }
}

public class DesignBaseSignalRHandler
{
    protected readonly IHubContext<ProjectHub> _projectHub;
    protected readonly CasterContext _db;

    public DesignBaseSignalRHandler(
        IHubContext<ProjectHub> projectHub,
        CasterContext db)
    {
        _projectHub = projectHub;
        _db = db;
    }

    protected async Task HandleCreateOrUpdate(
        Domain.Models.Design entity,
        string method,
        string[] modifiedProperties,
        CancellationToken cancellationToken)
    {
        var design = entity.ToDesign();
        var group = await GetGroup(entity, cancellationToken);
        await _projectHub.Clients.Group(group).SendAsync(method, design, modifiedProperties, cancellationToken);
    }

    protected async Task<string> GetGroup(Domain.Models.Design design, CancellationToken cancellationToken)
    {
        var projectId = await _db.Directories
            .Where(x => x.Id == design.DirectoryId)
            .Select(x => x.ProjectId)
            .SingleOrDefaultAsync(cancellationToken);

        return projectId.ToString();
    }
}