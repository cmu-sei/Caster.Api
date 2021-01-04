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
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Exceptions;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;
using System.Text.Json.Serialization;

namespace Caster.Api.Features.Directories
{
    public class GetByProject
    {
        [DataContract(Name="GetDirectoriesByProjectQuery")]
        public class Query : IRequest<Directory[]>
        {
            [JsonIgnore]
            public Guid ProjectId { get; set; }

            /// <summary>
            /// Whether or not to return only top-level Directories
            /// </summary>
            [DataMember]
            public bool IncludeDescendants { get; set; }

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

                await ValidateProject(request.ProjectId);

                var query = _db.Directories.Where(d => d.ProjectId == request.ProjectId);

                if (!request.IncludeDescendants)
                    query = query.Where(d => d.ParentId == null);

                var modifiedQuery = query.Expand(_mapper.ConfigurationProvider, request.IncludeRelated, request.IncludeFileContent);
                var directories = await modifiedQuery.ToArrayAsync();

                return directories;
            }

            private async Task ValidateProject(Guid projectId)
            {
                var project = await _db.Projects.FindAsync(projectId);

                if (project == null)
                    throw new EntityNotFoundException<Project>();
            }
        }
    }
}
