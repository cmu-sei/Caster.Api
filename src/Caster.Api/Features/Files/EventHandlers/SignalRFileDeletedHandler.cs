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

namespace Caster.Api.Features.Files.EventHandlers
{
    public class SignalRFileDeletedHandler : INotificationHandler<FileDeleted>
    {
        private readonly CasterContext _db;
        private readonly IMapper _mapper;
        private readonly IHubContext<ProjectHub> _projectHub;

        public SignalRFileDeletedHandler(
            CasterContext db,
            IMapper mapper,
            IHubContext<ProjectHub> projectHub)
        {
            _db = db;
            _mapper = mapper;
            _projectHub = projectHub;
        }

        public async Task Handle(FileDeleted notification, CancellationToken cancellationToken)
        {
            var projectId = await _db.Directories
                .Where(d => d.Id == notification.File.DirectoryId)
                .Select(d => d.ProjectId)
                .FirstOrDefaultAsync();

            await _projectHub.Clients.Group(projectId.ToString()).SendAsync("FileDeleted", notification.File.Id);
        }
    }
}
