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
using Caster.Api.Infrastructure.Authorization;
using System.Text.Json.Serialization;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Directories
{
    public class Get
    {
        [DataContract(Name = "GetDirectoryQuery")]
        public class Query : IRequest<Directory>
        {
            [JsonIgnore]
            public Guid Id { get; set; }

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

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, Directory>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Domain.Models.Directory>(request.Id, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], cancellationToken);

            public override async Task<Directory> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                var directory = await dbContext.Directories
                    .Expand(mapper.ConfigurationProvider, request.IncludeRelated, request.IncludeFileContent)
                    .SingleOrDefaultAsync(e => e.Id == request.Id, cancellationToken);

                if (directory == null)
                    throw new EntityNotFoundException<Directory>();

                return directory;
            }
        }
    }
}
