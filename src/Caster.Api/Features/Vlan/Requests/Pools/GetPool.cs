// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Microsoft.EntityFrameworkCore;
using AutoMapper.QueryableExtensions;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Shared;

namespace Caster.Api.Features.Vlan
{
    public class GetPool
    {
        [DataContract(Name = "GetPoolQuery")]
        public class Query : IRequest<Pool>
        {
            /// <summary>
            /// The Id of the pool being requested
            /// </summary>
            [DataMember]
            public Guid PoolId { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, Pool>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ViewVLANs], cancellationToken);

            public override async Task<Pool> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                var pool = await dbContext.Pools
                    .Where(p => p.Id == request.PoolId)
                    .ProjectTo<Pool>(mapper.ConfigurationProvider)
                    .FirstOrDefaultAsync(cancellationToken);

                if (pool == null)
                    throw new EntityNotFoundException<Pool>();

                return pool;
            }
        }
    }
}
