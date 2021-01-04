// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using AutoMapper;
using System.Runtime.Serialization;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Identity;

namespace Caster.Api.Features.UserPermissions
{
    public class Edit
    {
        [DataContract(Name="EditUserPermissionCommand")]
        public class Command : IRequest<UserPermission>
        {
            [DataMember]
            public Guid Id { get; set; }

            [DataMember]
            public Guid UserId { get; set; }

            [DataMember]
            public Guid PermissionId { get; set; }
        }

        public class Handler : IRequestHandler<Command, UserPermission>
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

            public async Task<UserPermission> Handle(Command request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded)
                    throw new ForbiddenException();

                var userPermission = await _db.UserPermissions.FindAsync(request.Id);

                if (userPermission == null)
                    throw new EntityNotFoundException<UserPermission>();

                _mapper.Map(request, userPermission);
                await _db.SaveChangesAsync();
                return _mapper.Map<UserPermission>(userPermission);
            }
        }
    }
}

