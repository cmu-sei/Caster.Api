// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using AutoMapper.QueryableExtensions;
using System.Runtime.Serialization;
using Caster.Api.Data;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Identity;

namespace Caster.Api.Features.Users
{
    public class GetUsersByPermission
    {
        [DataContract(Name="GetUsersByPermissionQuery")]
        public class Query : IRequest<User[]>
        {
            /// <summary>
            /// The Id of the Permission to query by
            /// </summary>
            [DataMember]
            public Guid PermissionId { get; set; }
        }

        public class Handler : IRequestHandler<Query, User[]>
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

            public async Task<User[]> Handle(Query request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded)
                    throw new ForbiddenException();

                var users =  await _db.Users.Where(u => u.UserPermissions.Any(up => up.PermissionId == request.PermissionId))
                    .ProjectTo<User>(_mapper.ConfigurationProvider, dest => dest.Permissions)
                    .ToArrayAsync();

                return users;
            }
        }
    }
}

