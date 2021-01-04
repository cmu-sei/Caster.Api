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
using Caster.Api.Domain.Models;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;
using System.Text.Json.Serialization;

namespace Caster.Api.Features.Files
{
    public class GetByDirectory
    {
        [DataContract(Name="GetFilesByDirectoryQuery")]
        public class Query : IRequest<File[]>
        {
            [JsonIgnore]
            public Guid DirectoryId { get; set; }

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

                await ValidateDirectory(request.DirectoryId);

                return await _db.Files
                    .GetAll(
                        configurationProvider: _mapper.ConfigurationProvider,
                        directoryId: request.DirectoryId,
                        includeDeleted: request.IncludeDeleted,
                        includeContent: request.IncludeContent)
                    .ToArrayAsync();
            }

            private async Task ValidateDirectory(Guid directoryId)
            {
                var directory = await _db.Directories.FindAsync(directoryId);

                if (directory == null)
                    throw new EntityNotFoundException<Directory>();
            }
        }
    }
}

