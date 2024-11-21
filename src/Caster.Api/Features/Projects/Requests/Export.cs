// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Authorization;
using System.Text.Json.Serialization;
using Caster.Api.Domain.Services;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Shared;

namespace Caster.Api.Features.Projects
{
    public class Export
    {
        [DataContract(Name = "ExportProjectQuery")]
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

        public class Handler(
            ICasterAuthorizationService authorizationService,
            CasterContext dbContext,
            IArchiveService archiveService) : BaseHandler<Query, ArchiveResult>
        {
            public override async Task Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Domain.Models.Project>(request.Id, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], cancellationToken);

            public override async Task<ArchiveResult> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                var project = await dbContext.Projects
                    .Include(e => e.Directories)
                        .ThenInclude(d => d.Files)
                    .Include(e => e.Directories)
                        .ThenInclude(d => d.Workspaces)
                    .SingleOrDefaultAsync(e => e.Id == request.Id, cancellationToken);

                if (project == null)
                    throw new EntityNotFoundException<Project>();

                return await archiveService.ArchiveProject(project, request.ArchiveType, request.IncludeIds);
            }
        }
    }
}
