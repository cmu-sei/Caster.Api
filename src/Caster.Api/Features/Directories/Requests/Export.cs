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
using Caster.Api.Features.Shared;

namespace Caster.Api.Features.Directories
{
    public class Export
    {
        [DataContract(Name = "ExportDirectoryQuery")]
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

        public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext, IArchiveService archiveService) : BaseHandler<Query, ArchiveResult>
        {
            public override async Task Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Domain.Models.Directory>(request.Id, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], cancellationToken);

            public override async Task<ArchiveResult> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                var directory = await dbContext.Directories
                    .SingleOrDefaultAsync(e => e.Id == request.Id, cancellationToken);

                if (directory == null)
                    throw new EntityNotFoundException<Directory>();

                var directories = await dbContext.GetDirectoryWithChildren(directory.Id, cancellationToken);
                return await archiveService.ArchiveDirectory(directory, request.ArchiveType, request.IncludeIds);
            }
        }
    }
}
