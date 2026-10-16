// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Data.Extensions;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Domain.Services.Inventory;
using Caster.Api.Features.Shared;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.Directories
{
    public class GetDiskManifest
    {
        /// <summary>
        /// The disk manifest an Export of the Directory would include, so it can be shown
        /// before exporting.
        /// </summary>
        [DataContract(Name = "GetDirectoryDiskManifestQuery")]
        public class Query : IRequest<DiskManifest>
        {
            [JsonIgnore]
            public Guid Id { get; set; }
        }

        public class Handler(
            ICasterAuthorizationService authorizationService,
            CasterContext dbContext,
            IArchiveService archiveService,
            IDiskManifestService diskManifestService) : BaseHandler<Query, DiskManifest>
        {
            /// <summary>
            /// The manifest describes the hypervisor's templates and ISOs, so it needs the same
            /// permission as the inventory itself.
            /// </summary>
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Domain.Models.Directory>(request.Id, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], cancellationToken) &&
                await authorizationService.Authorize([SystemPermission.ViewHosts], cancellationToken);

            public override async Task<DiskManifest> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                var directory = await dbContext.Directories
                    .SingleOrDefaultAsync(e => e.Id == request.Id, cancellationToken);

                if (directory == null)
                    throw new EntityNotFoundException<Directory>();

                await dbContext.GetDirectoryWithChildren(directory.Id, cancellationToken);

                return diskManifestService.Build(archiveService.ListFiles(directory, includeIds: false));
            }
        }
    }
}
