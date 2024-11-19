// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using Caster.Api.Infrastructure.Authorization;
using Microsoft.EntityFrameworkCore;
using AutoMapper.QueryableExtensions;
using System;
using System.Linq;
using FluentValidation;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Vlan
{
    public class GetPartitions
    {
        [DataContract(Name = "GetPartitionsQuery")]
        public class Query : IRequest<Partition[]>
        {
            [DataMember]
            public Guid? PoolId { get; set; }
        }

        public class Validator : AbstractValidator<Query>
        {
            public Validator(IValidationService validationService)
            {
                RuleFor(x => x.PoolId.Value).PoolExists(validationService).When(x => x.PoolId.HasValue);
            }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, Partition[]>
        {
            public override async Task Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermissions.ViewVLANs], cancellationToken);

            public override async Task<Partition[]> HandleRequest(Query query, CancellationToken cancellationToken)
            {
                var partitionQuery = dbContext.Partitions.AsQueryable();

                if (query.PoolId.HasValue)
                {
                    partitionQuery = partitionQuery.Where(x => x.PoolId == query.PoolId);
                }

                var partitions = await partitionQuery
                    .ProjectTo<Partition>(mapper.ConfigurationProvider)
                    .ToArrayAsync(cancellationToken);

                return partitions;
            }
        }
    }
}
