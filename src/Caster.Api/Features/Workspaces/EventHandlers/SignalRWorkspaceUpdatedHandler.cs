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

namespace Caster.Api.Features.Workspaces.EventHandlers
{
    public class SignalRWorkspaceUpdatedHandler : INotificationHandler<WorkspaceUpdated>
    {
        private readonly CasterContext _db;
        private readonly IMapper _mapper;
        private readonly IHubContext<ProjectHub> _projectHub;

        public SignalRWorkspaceUpdatedHandler(
            CasterContext db,
            IMapper mapper,
            IHubContext<ProjectHub> projectHub)
        {
            _db = db;
            _mapper = mapper;
            _projectHub = projectHub;
        }

        public async Task Handle(WorkspaceUpdated notification, CancellationToken cancellationToken)
        {
            var workspace = await _db.Workspaces
                .Where(d => d.Id == notification.WorkspaceId)
                .ProjectTo<Workspace>(_mapper.ConfigurationProvider)
                .FirstOrDefaultAsync();

            var projectId = await _db.Directories
                .Where(d => d.Id == workspace.DirectoryId)
                .Select(d => d.ProjectId)
                .FirstOrDefaultAsync();

            await _projectHub.Clients.Group(projectId.ToString()).SendAsync("WorkspaceUpdated", workspace);
        }
    }
}
