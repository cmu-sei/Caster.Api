// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.Inventory;
using Caster.Api.Features.Shared;
using Caster.Api.Infrastructure.Authorization;
using MediatR;

namespace Caster.Api.Features.Inventory
{
    public class Refresh
    {
        [DataContract(Name = "RefreshInventoryCommand")]
        public class Command : IRequest<InventoryRefreshResult>
        {
        }

        public class Handler(
            ICasterAuthorizationService authorizationService,
            IInventoryService inventoryService) : BaseHandler<Command, InventoryRefreshResult>
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ViewHosts], cancellationToken);

            public override async Task<InventoryRefreshResult> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                // Bounded synchronous refresh. We wait for the background read to
                // land so a caller that immediately re-reads the get endpoints sees
                // the new data, but we give up the wait - never the work - after
                // InventoryDefaults.ForceRefreshTimeout. A slow or unreachable
                // provider therefore cannot hang this request, and a disconnected
                // caller does not cancel the refresh.
                var completed = await inventoryService.ForceRefreshAsync(
                    InventoryDefaults.ForceRefreshTimeout, cancellationToken);

                return new InventoryRefreshResult
                {
                    Completed = completed,
                    LastUpdated = inventoryService.GetSnapshot().LastUpdated,
                    WaitSeconds = (int)InventoryDefaults.ForceRefreshTimeout.TotalSeconds
                };
            }
        }
    }
}
