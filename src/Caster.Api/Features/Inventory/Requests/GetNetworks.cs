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
    public class GetNetworks
    {
        [DataContract(Name = "GetInventoryNetworksQuery")]
        public class Query : IRequest<InventoryResult>
        {
        }

        public class Handler(
            ICasterAuthorizationService authorizationService,
            IInventoryService inventoryService) : BaseHandler<Query, InventoryResult>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ViewHosts], cancellationToken);

            public override Task<InventoryResult> HandleRequest(Query request, CancellationToken cancellationToken) =>
                Task.FromResult(InventoryResult.From(inventoryService.GetSnapshot(), InventoryCategory.Network));
        }
    }
}
