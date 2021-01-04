// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;
using System.Text.Json.Serialization;
using Caster.Api.Domain.Services;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Projects
{
    public class Export
    {
        [DataContract(Name="ExportProjectQuery")]
        public class Query : IRequest<ArchiveResult>
        {
            [JsonIgnore]
            public Guid Id { get; set; }
            public ArchiveType ArchiveType { get; set; }

            /// <summary>
            /// If true, Directory Ids will be appended to their names to be optionally preserved on Import
            /// </summary>
            public bool IncludeIds { get; set; }
        }

        public class Handler : IRequestHandler<Query, ArchiveResult>
        {
            private readonly CasterContext _db;
            private readonly IMapper _mapper;
            private readonly IAuthorizationService _authorizationService;
            private readonly ClaimsPrincipal _user;
            private readonly IArchiveService _archiveService;

            public Handler(
                CasterContext db,
                IMapper mapper,
                IAuthorizationService authorizationService,
                IIdentityResolver identityResolver,
                IArchiveService archiveService)
            {
                _db = db;
                _mapper = mapper;
                _authorizationService = authorizationService;
                _user = identityResolver.GetClaimsPrincipal();
                _archiveService = archiveService;
            }

            public async Task<ArchiveResult> Handle(Query request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                var project =  await _db.Projects
                    .Include(e => e.Directories)
                        .ThenInclude(d => d.Files)
                    .Include(e => e.Directories)
                        .ThenInclude(d => d.Workspaces)
                    .SingleOrDefaultAsync(e => e.Id == request.Id, cancellationToken);

                if (project == null)
                    throw new EntityNotFoundException<Project>();

                return await _archiveService.ArchiveProject(project, request.ArchiveType, request.IncludeIds);
            }
        }
    }
}
