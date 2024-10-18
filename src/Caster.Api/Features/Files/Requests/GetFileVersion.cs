// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using Microsoft.EntityFrameworkCore;
using AutoMapper.QueryableExtensions;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Files
{
    public class GetFileVersion
    {
        [DataContract(Name = "GetFileVersionQuery")]
        public class Query : IRequest<FileVersion>
        {
            /// <summary>
            /// The Id of the FileVersion to retrieve
            /// </summary>
            [DataMember]
            public Guid Id { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, FileVersion>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Domain.Models.FileVersion>(request.Id, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], cancellationToken);

            public override async Task<FileVersion> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                var fileVersion = await dbContext.FileVersions
                    .ProjectTo<FileVersion>(mapper.ConfigurationProvider, dest => dest.Content)
                    .SingleOrDefaultAsync(e => e.Id == request.Id, cancellationToken);

                if (fileVersion == null)
                    throw new EntityNotFoundException<FileVersion>();

                return fileVersion;
            }
        }
    }
}

