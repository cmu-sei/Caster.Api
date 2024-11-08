// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using AutoMapper;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using AutoMapper.QueryableExtensions;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;

namespace Caster.Api.Features.Groups
{
    public class Edit
    {
        [DataContract(Name = "EditGroupCommand")]
        public class Command : IRequest<Group>
        {
            [DataMember]
            public Guid Id { get; set; }

            /// <summary>
            /// Name of the group.
            /// </summary>
            [DataMember]
            public string Name { get; set; }
        }

        public class Handler : IRequestHandler<Command, Group>
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

            public async Task<Group> Handle(Command request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                var group = await _db.Groups.FindAsync(request.Id);

                if (group == null)
                    throw new EntityNotFoundException<Group>();

                _mapper.Map(request, group);
                await _db.SaveChangesAsync();
                return _mapper.Map<Group>(group);
            }
        }
    }
}
