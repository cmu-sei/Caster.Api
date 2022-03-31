// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Claims;
using System.Security.Principal;
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

        public class Handler : IRequestHandler<Query, Pool>
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

            public async Task<Pool> Handle(Query request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                var pool = await _db.Pools
                    .Where(p => p.Id == request.PoolId)
                    .ProjectTo<Pool>(_mapper.ConfigurationProvider)
                    .FirstOrDefaultAsync();

                if (pool == null)
                    throw new EntityNotFoundException<Pool>();

                return pool;
            }
        }
    }
}
