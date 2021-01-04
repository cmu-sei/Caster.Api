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
    public class SignalRDirectoryUpdatedHandler : INotificationHandler<DirectoryUpdated>
    {
        private readonly CasterContext _db;
        private readonly IMapper _mapper;
        private readonly IHubContext<ProjectHub> _projectHub;

        public SignalRDirectoryUpdatedHandler(
            CasterContext db,
            IMapper mapper,
            IHubContext<ProjectHub> projectHub)
        {
            _db = db;
            _mapper = mapper;
            _projectHub = projectHub;
        }

        public async Task Handle(DirectoryUpdated notification, CancellationToken cancellationToken)
        {
            var directory = await _db.Directories
                .Where(d => d.Id == notification.DirectoryId)
                .ProjectTo<Directory>(_mapper.ConfigurationProvider)
                .FirstOrDefaultAsync();

            await _projectHub.Clients.Group(directory.ProjectId.ToString()).SendAsync("DirectoryUpdated", directory);
        }
    }
}
