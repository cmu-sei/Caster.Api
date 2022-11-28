// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
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
using System.Linq;
using System.ComponentModel;
using EFCore.BulkExtensions;
using System.Collections.Generic;

namespace Caster.Api.Features.Vlan
{
    public class CreatePool
    {
        [DataContract(Name = "CreatePoolCommand")]
        public class Command : IRequest<Pool>
        {
            /// <summary>
            /// The Name of the Pool to create
            /// </summary>
            [DataMember]
            public string Name { get; set; }

            /// <summary>
            /// The Vlan Ids to reserve, preventing their use
            /// </summary>
            [DataMember]
            [DefaultValue(new int[] { 0, 1, 4095 })]
            public int[] ReservedVlanIds { get; set; } = new int[] { 0, 1, 4095 };
        }

        public class Handler : IRequestHandler<Command, Pool>
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

            public async Task<Pool> Handle(Command request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                var pool = _mapper.Map<Domain.Models.Pool>(request);
                _db.Pools.Add(pool);
                await _db.SaveChangesAsync(cancellationToken);

                var vlans = new List<Domain.Models.Vlan>();

                for (int i = 0; i < 4096; i++)
                {
                    vlans.Add(new Domain.Models.Vlan
                    {
                        VlanId = i,
                        Reserved = request.ReservedVlanIds.Contains(i),
                        PoolId = pool.Id
                    });
                }

                await _db.BulkInsertAsync(vlans,
                    new BulkConfig { PropertiesToExclude = new List<string> { nameof(Pool.Id) } }); // workaround until id properties in pgsql are fixed

                return _mapper.Map<Pool>(pool);
            }
        }
    }
}
