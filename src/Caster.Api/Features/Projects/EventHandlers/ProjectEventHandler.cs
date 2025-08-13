// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Caster.Api.Data;
using Caster.Api.Domain.Events;
using Caster.Api.Hubs;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.Projects.EventHandlers;

public class ProjectCreatedSignalRHandler(CasterContext _db, IMapper _mapper, IHubContext<ProjectHub> _projectHub) :
    ProjectBaseSignalRHandler(_db, _mapper, _projectHub),
    INotificationHandler<EntityCreated<Domain.Models.Project>>
{
    public async Task Handle(EntityCreated<Domain.Models.Project> notification, CancellationToken cancellationToken)
    {
        await base.Handle(notification.Entity, ProjectHubMethods.ProjectCreated, null, cancellationToken);
    }
}

public class ProjectDeletedSignalRHandler(IHubContext<ProjectHub> projectHub) :
    INotificationHandler<EntityDeleted<Domain.Models.Project>>
{
    public async Task Handle(EntityDeleted<Domain.Models.Project> notification, CancellationToken cancellationToken)
    {
        await projectHub.Clients.Group(ProjectHubMethods.GetProjectAdminGroup(notification.Entity.ProjectId)).SendAsync(ProjectHubMethods.ProjectDeleted, notification.Entity.Id);
    }
}

public class ProjectBaseSignalRHandler(CasterContext db, IMapper mapper, IHubContext<ProjectHub> projectHub)
{
    protected async Task Handle(Domain.Models.Project entity, string method, string[] modifiedProperties, CancellationToken cancellationToken)
    {
        var count = await db.Projects.LongCountAsync(cancellationToken);
        telemetryService.Projects.Record((int)count);

    }
}
