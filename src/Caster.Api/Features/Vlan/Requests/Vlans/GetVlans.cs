// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Runtime.Serialization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Data;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using AutoMapper.QueryableExtensions;
using System;
using System.Linq;
using System.Text.Json.Serialization;
using Caster.Api.Features.Shared.Services;
using FluentValidation;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Vlan
{
    public class GetVlans
    {
        [DataContract(Name = "GetVlansQuery")]
        public class Query : IRequest<Vlan[]>
        {
            [JsonIgnore]
            public Guid? PoolId { get; set; }

            [JsonIgnore]
            public Guid? PartitionId { get; set; }
        }

        public class Validator : AbstractValidator<Query>
        {
            public Validator(IValidationService validationService)
            {
                RuleFor(x => x.PoolId.Value).PoolExists(validationService).When(x => x.PoolId.HasValue);
                RuleFor(x => x.PartitionId.Value).PartitionExists(validationService).When(x => x.PartitionId.HasValue);
            }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, Vlan[]>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ViewVLANs], cancellationToken);

            public override async Task<Vlan[]> HandleRequest(Query query, CancellationToken cancellationToken)
            {
                var vlanQuery = dbContext.Vlans.AsQueryable();

                if (query.PartitionId.HasValue)
                {
                    vlanQuery = vlanQuery.Where(x => x.PartitionId == query.PartitionId.Value);
                }

                if (query.PoolId.HasValue)
                {
                    vlanQuery = vlanQuery.Where(x => x.PoolId == query.PoolId.Value);
                }

                return await vlanQuery
                    .ProjectTo<Vlan>(mapper.ConfigurationProvider)
                    .ToArrayAsync(cancellationToken);
            }
        }
    }
}
