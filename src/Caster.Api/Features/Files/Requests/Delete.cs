// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Files.Interfaces;
using Caster.Api.Domain.Services;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Identity;

namespace Caster.Api.Features.Files
{
    public class Delete
    {
        [DataContract(Name = "DeleteFileCommand")]
        public class Command : IRequest<File>, IFileDeleteRequest, IFileCommand
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
                var canLock = await CanLock(file.Id, cancellationToken);
                var userId = identityResolver.GetId();

                file.Lock(userId, canLock);

                try
                {
                    file.Delete(canLock);
                }
                finally
                {
                    file.Unlock(userId);
                }
            }
        }
    }
}

