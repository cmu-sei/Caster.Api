// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Events;
using MediatR;
using Caster.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Caster.Api.Features.Workspaces.EventHandlers
{
    public class SignalRWorkspaceSettingsUpdatedHandler : INotificationHandler<WorkspaceSettingsUpdated>
    {
        private readonly IHubContext<ProjectHub> _projectHub;

        public SignalRWorkspaceSettingsUpdatedHandler(
            IHubContext<ProjectHub> projectHub)
        {
            _projectHub = projectHub;
        }

        public async Task Handle(WorkspaceSettingsUpdated notification, CancellationToken cancellationToken)
        {
            await _projectHub.Clients.Group(nameof(HubGroups.WorkspacesAdmin)).SendAsync("WorkspaceSettingsUpdated", notification.LockingStatus);
        }
    }
}
