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
using Caster.Api.Features.Permissions;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Identity;

namespace Caster.Api.Features.Permissions
{
    public class GetPermissionsByUser
    {
        [DataContract(Name="GetPermissionsByUserQuery")]
        public class Query : IRequest<Permission[]>
        {
            /// <summary>
            /// The Id of the User to query by
            /// </summary>
            [DataMember]
            public Guid UserId { get; set; }
        }

        public class Handler : IRequestHandler<Query, Permission[]>
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

            public async Task<Permission[]> Handle(Query request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded)
                    throw new ForbiddenException();

                var permissions =  await _db.Permissions.Where(p => p.UserPermissions.Any(up => up.UserId == request.UserId))
                    .ProjectTo<Permission>(_mapper.ConfigurationProvider)
                    .ToArrayAsync();

                return permissions;
            }
        }
    }
}

