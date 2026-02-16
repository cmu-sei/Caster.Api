// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Caster.Api.Data;
using Crucible.Common.EntityEvents.Events;
using Caster.Api.Hubs;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.Projects.EventHandlers;

public class ProjectMembershipCreatedSignalRHandler(CasterContext _db, IMapper _mapper, IHubContext<ProjectHub> _projectHub) :
    ProjectMembershipBaseSignalRHandler(_db, _mapper, _projectHub),
    INotificationHandler<EntityCreated<Domain.Models.ProjectMembership>>
{
    public async Task Handle(EntityCreated<Domain.Models.ProjectMembership> notification, CancellationToken cancellationToken)
    {
        await base.Handle(notification.Entity, ProjectHubMethods.ProjectMembershipCreated, null, cancellationToken);
    }
}

public class ProjectMembershipUpdatedSignalRHandler(CasterContext _db, IMapper _mapper, IHubContext<ProjectHub> _projectHub) :
    ProjectMembershipBaseSignalRHandler(_db, _mapper, _projectHub),
    INotificationHandler<EntityUpdated<Domain.Models.ProjectMembership>>
{
    public async Task Handle(EntityUpdated<Domain.Models.ProjectMembership> notification, CancellationToken cancellationToken)
    {
        await base.Handle(notification.Entity, ProjectHubMethods.ProjectMembershipUpdated, notification.ModifiedProperties, cancellationToken);
    }
}

public class ProjectMembershipDeletedSignalRHandler(IHubContext<ProjectHub> projectHub) :
    INotificationHandler<EntityDeleted<Domain.Models.ProjectMembership>>
{
    public async Task Handle(EntityDeleted<Domain.Models.ProjectMembership> notification, CancellationToken cancellationToken)
    {
        await projectHub.Clients.Group(ProjectHubMethods.GetProjectAdminGroup(notification.Entity.ProjectId)).SendAsync(ProjectHubMethods.ProjectMembershipDeleted, notification.Entity.Id);
    }
}

public class ProjectMembershipBaseSignalRHandler(CasterContext db, IMapper mapper, IHubContext<ProjectHub> projectHub)
{
    protected async Task Handle(Domain.Models.ProjectMembership entity, string method, string[] modifiedProperties, CancellationToken cancellationToken)
    {
        var projectMembership = await db.ProjectMemberships
            .Where(x => x.Id == entity.Id)
            .ProjectTo<ProjectMembership>(mapper.ConfigurationProvider)
            .FirstOrDefaultAsync();

        await projectHub.Clients.Group(ProjectHubMethods.GetProjectAdminGroup(entity.ProjectId)).SendAsync(method, projectMembership, modifiedProperties, cancellationToken);
    }
}
