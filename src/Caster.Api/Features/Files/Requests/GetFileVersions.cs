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
using Caster.Api.Infrastructure.Exceptions;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;

namespace Caster.Api.Features.Files
{
    public class GetFileVersions
    {
        [DataContract(Name="GetFileVersionsQuery")]
        public class Query : IRequest<FileVersion[]>
        {
            /// <summary>
            /// The Id of the File to retrieve versions
            /// </summary>
            [DataMember]
            public Guid FileId { get; set; }
        }

        public class Handler : IRequestHandler<Query, FileVersion[]>
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
                _user = identityResolver.GetClaimsPrincipal() as ClaimsPrincipal;
            }

            public async Task<FileVersion[]> Handle(Query request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                var fileVersions = await _db.FileVersions
                    .Where(fv => fv.FileId == request.FileId)
                    .ProjectTo<FileVersion>(_mapper.ConfigurationProvider)
                    .ToArrayAsync();

                return fileVersions;
            }
        }
    }
}

