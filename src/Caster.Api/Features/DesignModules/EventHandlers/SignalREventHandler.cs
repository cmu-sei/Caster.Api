// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Caster.Api.Data;
using Caster.Api.Domain.Events;
using Caster.Api.Features.DesignModules;
using Caster.Api.Hubs;
using Caster.Api.Infrastructure.Extensions;
using MediatR;
using Microsoft.AspNetCore.SignalR;

namespace Caster.Api.Features.DesignModuleModules.EventHandlers;

public class DesignModuleCreatedSignalRHandler : DesignModuleBaseSignalRHandler, INotificationHandler<EntityCreated<Domain.Models.DesignModule>>
{
    public DesignModuleCreatedSignalRHandler(
        IHubContext<ProjectHub> projectHub,
        IMapper mapper,
        CasterContext db) : base(projectHub, mapper, db) { }

    public async Task Handle(EntityCreated<Domain.Models.DesignModule> notification, CancellationToken cancellationToken)
    {
        await base.HandleCreateOrUpdate(notification.Entity, DesignModuleHubMethods.Created, null, cancellationToken);
    }
}

public class DesignModuleUpdatedSignalRHandler : DesignModuleBaseSignalRHandler, INotificationHandler<EntityUpdated<Domain.Models.DesignModule>>
{
    public DesignModuleUpdatedSignalRHandler(
        IHubContext<ProjectHub> projectHub,
        IMapper mapper,
        CasterContext db) : base(projectHub, mapper, db) { }

    public async Task Handle(EntityUpdated<Domain.Models.DesignModule> notification, CancellationToken cancellationToken)
    {
        await base.HandleCreateOrUpdate(
            notification.Entity,
            DesignModuleHubMethods.Updated,
            notification.ModifiedProperties
                .Select(x =>
                {
                    if (x == "ValuesJson")
                    {
                        return nameof(Domain.Models.DesignModule.Values);
                    }
                    return x;
                }).ToArray(),
            cancellationToken);
    }
}

public class DesignModuleDeletedSignalRHandler : DesignModuleBaseSignalRHandler, INotificationHandler<EntityDeleted<Domain.Models.DesignModule>>
{
    public DesignModuleDeletedSignalRHandler(
        IHubContext<ProjectHub> projectHub,
        IMapper mapper,
        CasterContext db) : base(projectHub, mapper, db) { }

    public async Task Handle(EntityDeleted<Domain.Models.DesignModule> notification, CancellationToken cancellationToken)
    {
        var group = base.GetGroup(notification.Entity);
        await _projectHub.Clients.Group(group).SendAsync(DesignModuleHubMethods.Deleted, notification.Entity.Id);
    }
}

public class DesignModuleBaseSignalRHandler
{
    protected readonly IHubContext<ProjectHub> _projectHub;
    protected readonly IMapper _mapper;
    protected readonly CasterContext _db;

    public DesignModuleBaseSignalRHandler(
        IHubContext<ProjectHub> projectHub,
        IMapper mapper,
        CasterContext db)
    {
        _projectHub = projectHub;
        _mapper = mapper;
        _db = db;
    }

    protected async Task HandleCreateOrUpdate(
        Domain.Models.DesignModule entity,
        string method,
        string[] modifiedProperties,
        CancellationToken cancellationToken)
    {
        var designModule = _mapper.Map<DesignModule>(entity);
        var group = GetGroup(entity);
        await _projectHub.Clients.Group(group).SendAsync(method, designModule, modifiedProperties, cancellationToken);
    }

    protected string GetGroup(Domain.Models.DesignModule designModule)
    {
        return designModule.DesignId.ToString();
    }
}

static class DesignModuleHubMethods
{
    public const string Created = "DesignModuleCreated";
    public const string Updated = "DesignModuleUpdated";
    public const string Deleted = "DesignModuleDeleted";
}