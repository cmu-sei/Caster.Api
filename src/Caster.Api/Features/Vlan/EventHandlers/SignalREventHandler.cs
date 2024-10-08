// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Caster.Api.Data;
using Caster.Api.Domain.Events;
using Caster.Api.Hubs;
using Caster.Api.Infrastructure.Extensions;
using MediatR;
using Microsoft.AspNetCore.SignalR;

namespace Caster.Api.Features.Vlan.EventHandlers;

public class PoolCreatedSignalRHandler : VlanBaseSignalRHandler<Pool>, INotificationHandler<EntityCreated<Domain.Models.Pool>>
{
    public PoolCreatedSignalRHandler(
        IHubContext<ProjectHub> projectHub,
        IMapper mapper,
        CasterContext db) : base(projectHub, mapper, db) { }

    public async Task Handle(EntityCreated<Domain.Models.Pool> notification, CancellationToken cancellationToken)
    {
        await base.HandleCreateOrUpdate(notification.Entity, PoolHubMethods.Created, null, cancellationToken);
    }
}

public class PoolUpdatedSignalRHandler : VlanBaseSignalRHandler<Pool>, INotificationHandler<EntityUpdated<Domain.Models.Pool>>
{
    public PoolUpdatedSignalRHandler(
        IHubContext<ProjectHub> projectHub,
        IMapper mapper,
        CasterContext db) : base(projectHub, mapper, db) { }

    public async Task Handle(EntityUpdated<Domain.Models.Pool> notification, CancellationToken cancellationToken)
    {
        await base.HandleCreateOrUpdate(
            notification.Entity,
            PoolHubMethods.Updated,
            notification.ModifiedProperties,
            cancellationToken);
    }
}

public class PoolDeletedSignalRHandler : VlanBaseSignalRHandler<Pool>, INotificationHandler<EntityDeleted<Domain.Models.Pool>>
{
    public PoolDeletedSignalRHandler(
        IHubContext<ProjectHub> projectHub,
        IMapper mapper,
        CasterContext db) : base(projectHub, mapper, db) { }

    public async Task Handle(EntityDeleted<Domain.Models.Pool> notification, CancellationToken cancellationToken)
    {
        await _projectHub.Clients.Group(base.GetGroup()).SendAsync(PoolHubMethods.Deleted, notification.Entity.Id);
    }
}

public class PartitionCreatedSignalRHandler : VlanBaseSignalRHandler<Partition>, INotificationHandler<EntityCreated<Domain.Models.Partition>>
{
    public PartitionCreatedSignalRHandler(
        IHubContext<ProjectHub> projectHub,
        IMapper mapper,
        CasterContext db) : base(projectHub, mapper, db) { }

    public async Task Handle(EntityCreated<Domain.Models.Partition> notification, CancellationToken cancellationToken)
    {
        await base.HandleCreateOrUpdate(notification.Entity, PartitionHubMethods.Created, null, cancellationToken);
    }
}

public class PartitionUpdatedSignalRHandler : VlanBaseSignalRHandler<Partition>, INotificationHandler<EntityUpdated<Domain.Models.Partition>>
{
    public PartitionUpdatedSignalRHandler(
        IHubContext<ProjectHub> projectHub,
        IMapper mapper,
        CasterContext db) : base(projectHub, mapper, db) { }

    public async Task Handle(EntityUpdated<Domain.Models.Partition> notification, CancellationToken cancellationToken)
    {
        await base.HandleCreateOrUpdate(
            notification.Entity,
            PartitionHubMethods.Updated,
            notification.ModifiedProperties,
            cancellationToken);
    }
}

public class PartitionDeletedSignalRHandler : VlanBaseSignalRHandler<Partition>, INotificationHandler<EntityDeleted<Domain.Models.Partition>>
{
    public PartitionDeletedSignalRHandler(
        IHubContext<ProjectHub> projectHub,
        IMapper mapper,
        CasterContext db) : base(projectHub, mapper, db) { }

    public async Task Handle(EntityDeleted<Domain.Models.Partition> notification, CancellationToken cancellationToken)
    {
        await _projectHub.Clients.Group(base.GetGroup()).SendAsync(PartitionHubMethods.Deleted, notification.Entity.Id);
    }
}

public class VlanCreatedSignalRHandler : VlanBaseSignalRHandler<Vlan>, INotificationHandler<EntityCreated<Domain.Models.Vlan>>
{
    public VlanCreatedSignalRHandler(
        IHubContext<ProjectHub> projectHub,
        IMapper mapper,
        CasterContext db) : base(projectHub, mapper, db) { }

    public async Task Handle(EntityCreated<Domain.Models.Vlan> notification, CancellationToken cancellationToken)
    {
        await base.HandleCreateOrUpdate(notification.Entity, VlanHubMethods.Created, null, cancellationToken);
    }
}

public class VlanUpdatedSignalRHandler : VlanBaseSignalRHandler<Vlan>, INotificationHandler<EntityUpdated<Domain.Models.Vlan>>
{
    public VlanUpdatedSignalRHandler(
        IHubContext<ProjectHub> projectHub,
        IMapper mapper,
        CasterContext db) : base(projectHub, mapper, db) { }

    public async Task Handle(EntityUpdated<Domain.Models.Vlan> notification, CancellationToken cancellationToken)
    {
        await base.HandleCreateOrUpdate(
            notification.Entity,
            VlanHubMethods.Updated,
            notification.ModifiedProperties,
            cancellationToken);
    }
}

public class VlanDeletedSignalRHandler : VlanBaseSignalRHandler<Vlan>, INotificationHandler<EntityDeleted<Domain.Models.Vlan>>
{
    public VlanDeletedSignalRHandler(
        IHubContext<ProjectHub> projectHub,
        IMapper mapper,
        CasterContext db) : base(projectHub, mapper, db) { }

    public async Task Handle(EntityDeleted<Domain.Models.Vlan> notification, CancellationToken cancellationToken)
    {
        await _projectHub.Clients.Group(base.GetGroup()).SendAsync(VlanHubMethods.Deleted, notification.Entity.Id);
    }
}

public class VlanBaseSignalRHandler<T>
{
    protected readonly IHubContext<ProjectHub> _projectHub;
    protected readonly IMapper _mapper;
    protected readonly CasterContext _db;

    public VlanBaseSignalRHandler(
        IHubContext<ProjectHub> projectHub,
        IMapper mapper,
        CasterContext db)
    {
        _projectHub = projectHub;
        _mapper = mapper;
        _db = db;
    }

    protected async Task HandleCreateOrUpdate(
        object model,
        string method,
        string[] modifiedProperties,
        CancellationToken cancellationToken)
    {
        var viewModel = _mapper.Map<T>(model);
        await _projectHub.Clients.Group(this.GetGroup()).SendAsync(method, viewModel, modifiedProperties, cancellationToken);
    }

    protected string GetGroup()
    {
        return HubGroups.VlansAdmin.ToString();
    }
}

static class PoolHubMethods
{
    public const string Created = "PoolCreated";
    public const string Updated = "PoolUpdated";
    public const string Deleted = "PoolDeleted";
}

static class PartitionHubMethods
{
    public const string Created = "PartitionCreated";
    public const string Updated = "PartitionUpdated";
    public const string Deleted = "PartitionDeleted";
}

static class VlanHubMethods
{
    public const string Created = "VlanCreated";
    public const string Updated = "VlanUpdated";
    public const string Deleted = "VlanDeleted";
}