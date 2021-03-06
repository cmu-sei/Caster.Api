// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using AutoMapper.QueryableExtensions;
using System.Runtime.Serialization;
using Caster.Api.Data;
using Caster.Api.Infrastructure.Exceptions;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Security.Principal;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;

namespace Caster.Api.Features.Hosts
{
    public class Get
    {
        [DataContract(Name="GetHostQuery")]
        public class Query : IRequest<Host>
        {
            /// <summary>
            /// The Id of the Host to retrieve
            /// </summary>
            [DataMember]
            public Guid Id { get; set; }
        }

        public class Handler : IRequestHandler<Query, Host>
        {
            private readonly CasterContext _db;
            private readonly IMapper _mapper;
            private readonly IAuthorizationService _authorizationService;
            private readonly ClaimsPrincipal _user;

            public Handler(CasterContext db, IMapper mapper, IAuthorizationService authorizationService, IIdentityResolver identityResolver)
            {
                _db = db;
                _mapper = mapper;
                _authorizationService = authorizationService;
                _user = identityResolver.GetClaimsPrincipal();
            }

            public async Task<Host> Handle(Query request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded)
                    throw new ForbiddenException();

                var Host =  await _db.Hosts
                    .ProjectTo<Host>(_mapper.ConfigurationProvider)
                    .SingleOrDefaultAsync(e => e.Id == request.Id);

                if (Host == null)
                    throw new EntityNotFoundException<Host>();

                return Host;
            }
        }
    }
}

