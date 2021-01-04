// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using System.Runtime.Serialization;
using AutoMapper;
using Caster.Api.Data;
using System;
using Microsoft.AspNetCore.Authorization;
using System.Security.Principal;
using System.Security.Claims;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;

namespace Caster.Api.Features.Hosts
{
    public class Create
    {
        [DataContract(Name="CreateHostCommand")]
        public class Command : IRequest<Host>
        {
            /// <summary>
            /// Name of the Host
            /// </summary>
            [DataMember]
            public string Name { get; set; }

            /// <summary>
            /// Name of the datastore to use with this Host
            /// </summary>
            [DataMember]
            public string Datastore { get; set; }

            /// <summary>
            /// Maximum number of mahcines to deploy to this Host
            /// </summary>
            [DataMember]
            public int MaximumMachines { get; set; }

            /// <summary>
            /// True if this Host should be selected for deployments
            /// </summary>
            [DataMember]
            public bool Enabled { get; set; }

            /// <summary>
            /// True if this Host should not be automatically selected for deployments and only used for development purposes, manually
            /// </summary>
            [DataMember]
            public bool Development { get; set; }

            /// <summary>
            /// The Id of the Project to assign this Host to
            /// </summary>
            [DataMember]
            public Guid? ProjectId { get; set; }
        }

        public class Handler : IRequestHandler<Command, Host>
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

            public async Task<Host> Handle(Command request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded)
                    throw new ForbiddenException();

                var Host = _mapper.Map<Domain.Models.Host>(request);
                await _db.Hosts.AddAsync(Host);
                await _db.SaveChangesAsync();
                return _mapper.Map<Host>(Host);
            }
        }
    }
}
