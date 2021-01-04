// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

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
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Infrastructure.Identity;

namespace Caster.Api.Features.Permissions
{
    public class GetMine
    {
        [DataContract(Name="GetMyPermissionsQuery")]
        public class Query : IRequest<Permission[]>
        {
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
                return await _db.UserPermissions
                    .Where(w => w.UserId == _user.GetId())
                    .Select(x => x.Permission)
                    .ProjectTo<Permission>(_mapper.ConfigurationProvider)
                    .ToArrayAsync();
            }
        }
    }
}

