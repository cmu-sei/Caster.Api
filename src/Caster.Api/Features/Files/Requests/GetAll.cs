// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using Microsoft.EntityFrameworkCore;
using AutoMapper.QueryableExtensions;
using System.Runtime.Serialization;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Identity;

namespace Caster.Api.Features.Files
{
    public class GetAll
    {
        [DataContract(Name="GetFilesQuery")]
        public class Query : IRequest<File[]>
        {
            /// <summary>
            /// Whether or not to retrieve file content.
            /// </summary>
            [DataMember]
            public bool IncludeContent { get; set; }

            /// <summary>
            /// Whether or not to retrieve deleted files.
            /// </summary>
            [DataMember]
            public bool IncludeDeleted { get; set; }
        }

        public class Handler : IRequestHandler<Query, File[]>
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

            public async Task<File[]> Handle(Query request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                return await _db.Files
                    .GetAll(
                        configurationProvider: _mapper.ConfigurationProvider,
                        includeDeleted: request.IncludeDeleted,
                        includeContent: request.IncludeContent)
                    .ToArrayAsync();
            }
        }
    }
}

