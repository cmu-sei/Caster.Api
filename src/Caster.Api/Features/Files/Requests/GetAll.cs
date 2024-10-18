// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Files
{
    public class GetAll
    {
        [DataContract(Name = "GetFilesQuery")]
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

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, File[]>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ViewProjects], cancellationToken);

            public override async Task<File[]> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                return await dbContext.Files
                    .GetAll(
                        configurationProvider: mapper.ConfigurationProvider,
                        includeDeleted: request.IncludeDeleted,
                        includeContent: request.IncludeContent)
                    .ToArrayAsync(cancellationToken);
            }
        }
    }
}

