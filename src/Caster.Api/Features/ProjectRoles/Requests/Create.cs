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

namespace Caster.Api.Features.ProjectRoles
{
    public class Create
    {
        [DataContract(Name = "CreateProjectRoleCommand")]
        public class Command : IRequest<ProjectRole>
        {
            [DataMember]
            public Guid Id { get; set; }

            [DataMember]
            public string Name { get; set; }

            [DataMember]
            public Domain.Models.ProjectPermissions[] Permissions { get; set; }
        }

        public class Handler : IRequestHandler<Command, ProjectRole>
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

            public async Task<ProjectRole> Handle(Command request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded)
                    throw new ForbiddenException();

                var projectRole = _mapper.Map<Domain.Models.ProjectRole>(request);
                await _db.ProjectRoles.AddAsync(projectRole);
                await _db.SaveChangesAsync();
                return _mapper.Map<ProjectRole>(projectRole);
            }
        }
    }
}

