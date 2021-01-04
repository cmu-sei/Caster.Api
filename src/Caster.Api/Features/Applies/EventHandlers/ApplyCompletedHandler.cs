// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Events;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using MediatR;

namespace Caster.Api.Features.Applies.EventHandlers
{
    public class ApplyCompletedHandler : INotificationHandler<ApplyCompleted>
    {
        private readonly IPlayerSyncService _playerSyncService;
        private readonly CasterContext _dbContext;

        public ApplyCompletedHandler(IPlayerSyncService playerSyncService, CasterContext dbContext)
        {
            _playerSyncService = playerSyncService;
            _dbContext = dbContext;
        }

        public async Task Handle(ApplyCompleted notification, CancellationToken cancellationToken)
        {
            await _playerSyncService.AddAsync(notification.Workspace.Id);
            await this.ProcessRemovedResources(notification.Workspace);
        }

        private async Task ProcessRemovedResources(Workspace workspace)
        {
            var removedResources = workspace.GetRemovedResources();
            var resourcesToSync = removedResources
                .Where(r => r.IsVirtualMachine())
                .Select(r => new RemovedResource { Id = r.Id });

            await _dbContext.RemovedResources.AddRangeAsync(resourcesToSync);
            await _dbContext.SaveChangesAsync();

            if (resourcesToSync.Any())
                _playerSyncService.CheckRemovedResources();
        }
    }
}
