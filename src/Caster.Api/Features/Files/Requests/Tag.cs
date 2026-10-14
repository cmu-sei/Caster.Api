// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using Microsoft.EntityFrameworkCore;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;
using System.Collections.Generic;

namespace Caster.Api.Features.Files
{
    public class Tag
    {
        [DataContract(Name = "TagFileCommand")]
        public class Command : IRequest<FileVersion[]>
        {
            /// <summary>
            /// Tag to apply.
            /// </summary>
            [DataMember]
            public string Tag { get; set; }

            /// <summary>
            /// ID's of the files to tag.
            /// </summary>
            [DataMember]
            public Guid[] FileIds { get; set; }

        }

        public class Handler(
            ICasterAuthorizationService authorizationService,
            IMapper mapper,
            CasterContext dbContext,
            IIdentityResolver identityResolver) : BaseHandler<Command, FileVersion[]>
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken)
            {
                if (request.FileIds == null || request.FileIds.Length == 0)
                    return false;

                foreach (var fileId in request.FileIds.Distinct())
                {
                    if (!await authorizationService.Authorize<Domain.Models.File>(fileId, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken))
                        return false;
                }

                return true;
            }

            public override async Task<FileVersion[]> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var dateTagged = DateTime.UtcNow;
                var tag = request.Tag;

                var files = await dbContext.Files
                    .Where(f => request.FileIds.Contains(f.Id))
                    .ToArrayAsync(cancellationToken);

                var fileIds = request.FileIds.Distinct().ToArray();
                if (files.Length != fileIds.Length)
                {
                    var missingId = fileIds.First(id => files.All(file => file.Id != id));
                    throw new EntityNotFoundException<File>($"File {missingId} could not be found.");
                }

                foreach (var file in files)
                {
                    file.Tag(tag, identityResolver.GetId(), dateTagged);
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                return await dbContext.FileVersions
                    .Where(fileVersion => fileVersion.Tag == request.Tag && request.FileIds.Contains(fileVersion.FileId))
                    .ProjectTo<FileVersion>(mapper.ConfigurationProvider)
                    .ToArrayAsync(cancellationToken);
            }

        }
    }
}
