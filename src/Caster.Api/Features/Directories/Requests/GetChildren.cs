// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;
using System.Text.Json.Serialization;
using Caster.Api.Data.Extensions;

namespace Caster.Api.Features.Directories
{
    public class GetChildren
    {
        [DataContract(Name="GetDirectoryChildrenQuery")]
        public class Query : IRequest<Directory[]>
        {
            [JsonIgnore]
            public Guid DirectoryId { get; set; }

            /// <summary>
            /// Whether or not to return only top-level Directories
            /// </summary>
            [DataMember]
            public bool IncludeDescendants { get; set; } = true;

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

                var directory = await _db.Directories.FindAsync(request.DirectoryId);

                if (directory == null)
                    throw new EntityNotFoundException<Directory>();

                IQueryable<Domain.Models.Directory> query;

                if (request.IncludeDescendants)
                {
                    query = _db.Directories.GetChildren(directory, false);
                }
                else
                {
                    query = _db.Directories.Where(d => d.ParentId == directory.Id);
                }

                var modifiedQuery = query.Expand(_mapper.ConfigurationProvider, request.IncludeRelated, request.IncludeFileContent);
                var directories = await modifiedQuery.ToArrayAsync();

                return directories;
            }
        }
    }
}
