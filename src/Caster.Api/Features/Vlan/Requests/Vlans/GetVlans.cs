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

        public class Handler : IRequestHandler<Query, Vlan[]>
        {
            private readonly CasterContext _db;
            private readonly IMapper _mapper;
            private readonly IAuthorizationService _authorizationService;
            private readonly ClaimsPrincipal _user;

            public Handler(
                CasterContext db,
                IMapper mapper,
                IAuthorizationService authorizationService,
                IIdentityResolver identityResolver)
            {
                _db = db;
                _mapper = mapper;
                _authorizationService = authorizationService;
                _user = identityResolver.GetClaimsPrincipal();
            }

            public async Task<Vlan[]> Handle(Query query, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                var vlanQuery = _db.Vlans.AsQueryable();

                if (query.PartitionId.HasValue)
                {
                    vlanQuery = vlanQuery.Where(x => x.PartitionId == query.PartitionId.Value);
                }

                if (query.PoolId.HasValue)
                {
                    vlanQuery = vlanQuery.Where(x => x.PoolId == query.PoolId.Value);
                }

                return await vlanQuery
                    .ProjectTo<Vlan>(_mapper.ConfigurationProvider)
                    .ToArrayAsync();
            }
        }
    }
}
