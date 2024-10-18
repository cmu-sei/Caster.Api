// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using System.Runtime.Serialization;
using Caster.Api.Domain.Services;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Features.Files.Interfaces;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Files
{
    public class Lock
    {
        [DataContract(Name = "LockFileCommand")]
        public class Command : FileMetadataUpdateRequest, IRequest<File>, IFileCommand
        {
            public Guid Id { get; set; }
        }

        public class Handler(
            CasterContext dbContext,
            ILockService lockService,
            IGetFileQuery fileQuery,
            ICasterAuthorizationService authorizationService,
            IIdentityResolver identityResolver) : FileCommandHandler<Command, File>(dbContext, lockService, fileQuery, authorizationService)
        {

            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await AuthorizationService.Authorize<Domain.Models.File>(request.Id, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

            protected override async Task PerformOperation(Domain.Models.File file, CancellationToken cancellationToken)
            {
                file.Lock(identityResolver.GetId(), canLock: await CanLock(file.Id, cancellationToken));
            }
        }
    }
}
