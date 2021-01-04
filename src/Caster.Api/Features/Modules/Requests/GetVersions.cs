// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using Microsoft.EntityFrameworkCore;
using AutoMapper.QueryableExtensions;
using System.Runtime.Serialization;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Identity;

namespace Caster.Api.Features.Modules
{
    public class GetVersions
    {
        [DataContract(Name="GetVersionsQuery")]
        public class Query : IRequest<ModuleVersion[]>
        {
            /// <summary>
            /// The Id of the Module to retrieve versions for
            /// </summary>
            [DataMember]
            public Guid ModuleId { get; set; }
        }

        public class Handler : IRequestHandler<Query, ModuleVersion[]>
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

            public async Task<ModuleVersion[]> Handle(Query request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                return await _db.ModuleVersions.Where(v => v.ModuleId == request.ModuleId)
                    .ProjectTo<ModuleVersion>(_mapper.ConfigurationProvider)
                    .OrderByDescending(v => v.DateCreated)
                    .ToArrayAsync();
            }
        }
    }
}
