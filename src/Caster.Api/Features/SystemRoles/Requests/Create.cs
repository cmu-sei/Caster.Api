// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using System.Runtime.Serialization;
using AutoMapper;
using Caster.Api.Data;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Identity;

namespace Caster.Api.Features.SystemRoles
{
    public class Create
    {
        [DataContract(Name = "CreateSystemRoleCommand")]
        public class Command : IRequest<SystemRole>
        {
            [DataMember]
            public Guid Id { get; set; }

            [DataMember]
            public string Name { get; set; }

            [DataMember]

            public bool AllPermissions { get; set; }

            [DataMember]
            public Domain.Models.SystemPermissions[] Permissions { get; set; }
        }

        public class Handler : IRequestHandler<Command, SystemRole>
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

            public async Task<SystemRole> Handle(Command request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded)
                    throw new ForbiddenException();

                var SystemRole = _mapper.Map<Domain.Models.SystemRole>(request);
                await _db.SystemRoles.AddAsync(SystemRole);
                await _db.SaveChangesAsync();
                return _mapper.Map<SystemRole>(SystemRole);
            }
        }
    }
}

