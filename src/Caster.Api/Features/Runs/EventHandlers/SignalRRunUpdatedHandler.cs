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

namespace Caster.Api.Features.Runs.EventHandlers
{
    public class SignalRRunUpdatedHandler : INotificationHandler<IRunUpdate>
    {
        private readonly CasterContext _db;
        private readonly IMapper _mapper;
        private readonly IHubContext<ProjectHub> _projectHub;

        public SignalRRunUpdatedHandler(
            CasterContext db,
            IMapper mapper,
            IHubContext<ProjectHub> projectHub)
        {
            _db = db;
            _mapper = mapper;
            _projectHub = projectHub;
        }

        public async Task Handle(IRunUpdate notification, CancellationToken cancellationToken)
        {
            var run = await _db.Runs
                .Where(r => r.Id == notification.RunId)
                .ProjectTo<Run>(_mapper.ConfigurationProvider)
                .FirstOrDefaultAsync();

            await _projectHub.Clients.Groups(run.WorkspaceId.ToString(), nameof(HubGroups.WorkspacesAdmin)).SendAsync("RunUpdated", run);
        }
    }
}
