// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using Caster.Api.Infrastructure.Authorization;
using Microsoft.EntityFrameworkCore;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Vlan
{
    public class GetPools
    {
        [DataContract(Name = "GetPoolsQuery")]
        public class Query : IRequest<Pool[]>
        {
        }

        public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext) : BaseHandler<Query, Pool[]>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ViewVLANs], cancellationToken);

            public override async Task<Pool[]> HandleRequest(Query poolRequest, CancellationToken cancellationToken)
            {
                var pools = await dbContext.Pools
                    .ProjectToPool()
                    .ToArrayAsync(cancellationToken);

                return pools;
            }
        }
    }
}
