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

namespace Caster.Api.Features.Directories.EventHandlers
{
    public class SignalRDirectoryDeletedHandler : INotificationHandler<DirectoryDeleted>
    {
        private readonly IHubContext<ProjectHub> _projectHub;

        public SignalRDirectoryDeletedHandler(
            IHubContext<ProjectHub> projectHub)
        {
            _projectHub = projectHub;
        }

        public async Task Handle(DirectoryDeleted notification, CancellationToken cancellationToken)
        {
            await _projectHub.Clients.Group(notification.Directory.ProjectId.ToString()).SendAsync("DirectoryDeleted", notification.Directory.Id);
        }
    }
}
