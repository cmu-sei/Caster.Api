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

namespace Caster.Api.Features.Variables.EventHandlers;

public class VariableCreatedSignalRHandler : VariableBaseSignalRHandler, INotificationHandler<EntityCreated<Domain.Models.Variable>>
{
    public VariableCreatedSignalRHandler(
        IHubContext<ProjectHub> projectHub,
        CasterContext db) : base(projectHub, db) { }

    public async Task Handle(EntityCreated<Domain.Models.Variable> notification, CancellationToken cancellationToken)
    {
        await base.HandleCreateOrUpdate(notification.Entity, ProjectHubMethods.VariableCreated, null, cancellationToken);
    }
}

public class VariableUpdatedSignalRHandler : VariableBaseSignalRHandler, INotificationHandler<EntityUpdated<Domain.Models.Variable>>
{
    public VariableUpdatedSignalRHandler(
        IHubContext<ProjectHub> projectHub,
        CasterContext db) : base(projectHub, db) { }

    public async Task Handle(EntityUpdated<Domain.Models.Variable> notification, CancellationToken cancellationToken)
    {
        await base.HandleCreateOrUpdate(
            notification.Entity,
            ProjectHubMethods.VariableUpdated,
            notification.Entity.GetModifiedProperties(notification.ModifiedProperties),
            cancellationToken);
    }
}

public class VariableDeletedSignalRHandler : VariableBaseSignalRHandler, INotificationHandler<EntityDeleted<Domain.Models.Variable>>
{
    public VariableDeletedSignalRHandler(
        IHubContext<ProjectHub> projectHub,
        CasterContext db) : base(projectHub, db) { }

    public async Task Handle(EntityDeleted<Domain.Models.Variable> notification, CancellationToken cancellationToken)
    {
        var group = base.GetGroup(notification.Entity);
        await _projectHub.Clients.Group(group).SendAsync(ProjectHubMethods.VariableDeleted, notification.Entity.Id);
    }
}

public class VariableBaseSignalRHandler
{
    protected readonly IHubContext<ProjectHub> _projectHub;
    protected readonly CasterContext _db;

    public VariableBaseSignalRHandler(
        IHubContext<ProjectHub> projectHub,
        CasterContext db)
    {
        _projectHub = projectHub;
        _db = db;
    }

    protected async Task HandleCreateOrUpdate(
        Domain.Models.Variable entity,
        string method,
        string[] modifiedProperties,
        CancellationToken cancellationToken)
    {
        var variable = entity.ToVariable();
        var group = GetGroup(entity);
        await _projectHub.Clients.Group(group).SendAsync(method, variable, modifiedProperties, cancellationToken);
    }

    protected string GetGroup(Domain.Models.Variable variable)
    {
        return variable.DesignId.ToString();
    }
}