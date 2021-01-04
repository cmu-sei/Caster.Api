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
    public class SignalRWorkspaceDeletedHandler : INotificationHandler<WorkspaceDeleted>
    {
        private readonly CasterContext _db;
        private readonly IHubContext<ProjectHub> _projectHub;

        public SignalRWorkspaceDeletedHandler(
            CasterContext db,
            IHubContext<ProjectHub> projectHub)
        {
            _db = db;
            _projectHub = projectHub;
        }

        public async Task Handle(WorkspaceDeleted notification, CancellationToken cancellationToken)
        {
            var projectId = await _db.Directories
                .Where(d => d.Id == notification.Workspace.DirectoryId)
                .Select(d => d.ProjectId)
                .FirstOrDefaultAsync();

            await _projectHub.Clients.Group(projectId.ToString()).SendAsync("WorkspaceDeleted", notification.Workspace.Id);
        }
    }
}
