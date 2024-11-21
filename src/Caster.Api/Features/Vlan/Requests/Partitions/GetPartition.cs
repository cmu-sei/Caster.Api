// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

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
using System;
using System.Linq;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Vlan
{
    public class GetPartition
    {
        [DataContract(Name = "GetPartitionQuery")]
        public class Query : IRequest<Partition>
        {
            public Guid Id { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, Partition>
        {
            public override async Task Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ViewVLANs], cancellationToken);

            public override async Task<Partition> HandleRequest(Query query, CancellationToken cancellationToken)
            {
                var partition = await dbContext.Partitions
                    .Where(x => x.Id == query.Id)
                    .ProjectTo<Partition>(mapper.ConfigurationProvider)
                    .FirstOrDefaultAsync(cancellationToken);

                if (partition == null)
                    throw new EntityNotFoundException<Partition>();

                return partition;
            }
        }
    }
}
