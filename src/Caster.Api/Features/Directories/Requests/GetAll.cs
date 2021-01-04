// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;

namespace Caster.Api.Features.Directories
{
    public class GetAll
    {
        [DataContract(Name="GetDirectoriesQuery")]
        public class Query : IRequest<Directory[]>
        {
            /// <summary>
            /// Whether or not to return related objects (Files, Workspaces)
            /// </summary>
            [DataMember]
            public bool IncludeRelated { get; set; }

            /// <summary>
            /// Whether or not to include contents of returned Files. Ignored if IncludeRelated is false
            /// </summary>
            [DataMember]
            public bool IncludeFileContent { get; set; }
        }

        public class Handler : IRequestHandler<Query, Directory[]>
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

            public async Task<Directory[]> Handle(Query request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                return await _db.Directories
                    .Expand(_mapper.ConfigurationProvider, request.IncludeRelated, request.IncludeFileContent)
                    .ToArrayAsync();
            }
        }
    }
}

