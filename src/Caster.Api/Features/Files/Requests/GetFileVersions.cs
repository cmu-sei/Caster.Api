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
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Files
{
    public class GetFileVersions
    {
        [DataContract(Name = "GetFileVersionsQuery")]
        public class Query : IRequest<FileVersion[]>
        {
            /// <summary>
            /// The Id of the File to retrieve versions
            /// </summary>
            [DataMember]
            public Guid FileId { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbcontext) : BaseHandler<Query, FileVersion[]>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Domain.Models.File>(request.FileId, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], cancellationToken);

            public override async Task<FileVersion[]> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                var fileVersions = await dbcontext.FileVersions
                    .Where(fv => fv.FileId == request.FileId)
                    .ProjectTo<FileVersion>(mapper.ConfigurationProvider)
                    .ToArrayAsync(cancellationToken);

                return fileVersions;
            }
        }
    }
}

