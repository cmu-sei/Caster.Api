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

namespace Caster.Api.Features.Vlan
{
    public class CreateVlan
    {
        [DataContract(Name="CreateVlanCommand")]
        public class Command : IRequest<Vlan>
        {
            /// <summary>
            /// The vlan that is being created.
            /// </summary>
            [DataMember]
            public int Vlan { get; set; }

            /// <summary>
            /// The partition this vlan belongs to.
            /// </summary>
            [DataMember]
            public Guid PartitionId { get; set; }

            /// <summary>
            /// The pool of the partition this vlan belongs to.
            /// </summary>
            [DataMember]
            public Guid PoolId { get; set; }
        }

        public class Handler : IRequestHandler<Command, Vlan>
        {
            private readonly CasterContext _db;
            private readonly IMapper _mapper;
            private readonly IAuthorizationService _authorizationService;
            private readonly ClaimsPrincipal _user;
            private readonly IIdentityResolver _identityResolver;

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
                _identityResolver = identityResolver;
            }

            public async Task<Vlan> Handle(Command vlanRequest, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                var vlan = _mapper.Map<Domain.Models.Vlan>(vlanRequest);
                await _db.Vlans.AddAsync(vlan);
                await _db.SaveChangesAsync();

                return _mapper.Map<Vlan>(vlan);
            }
        }
    }
}
