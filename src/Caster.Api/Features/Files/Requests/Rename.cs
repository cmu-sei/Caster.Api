// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Features.Files.Interfaces;
using System.Text.Json.Serialization;
using Caster.Api.Infrastructure.Authorization;

namespace Caster.Api.Features.Files
{
    public class Rename
    {
        [DataContract(Name = "RenameFileCommand")]
        public class Command : FileUpdateRequest, IRequest<File>, IFileCommand
        {
            [JsonIgnore]
            public Guid Id { get; set; }

            /// <summary>
            /// New Name for the file.
            /// </summary>
            [DataMember]
            public string Name { get; set; }

            [JsonIgnore]
            public override string Content { get; set; }
        }

        public class Handler(
            CasterContext dbContext,
            ILockService lockService,
            IGetFileQuery fileQuery,
            ICasterAuthorizationService authorizationService,
            IIdentityResolver identityResolver) : FileCommandHandler<Command, File>(dbContext, lockService, fileQuery, authorizationService)
        {
            private Command _request { get; set; }

            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await AuthorizationService.Authorize<Domain.Models.File>(request.Id, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

            public override async Task<File> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                _request = request;
                return await base.HandleRequest(request, cancellationToken);
            }

            protected override async Task PerformOperation(Domain.Models.File file, CancellationToken cancellationToken)
            {
                var canLock = await CanLock(file.Id, cancellationToken);
                var userId = identityResolver.GetId();
                var isNotAlreadyLocked = userId != file.LockedById;

                if (isNotAlreadyLocked)
                {
                    try
                    {
                        file.Lock(userId, canLock);
                    }
                    catch (FileConflictException)
                    {
                        throw new FileConflictException("Cannot rename a file while it's being edited or locked by another user.");
                    }
                }

                try
                {
                    file.Name = _request.Name;
                    file.Save(userId, canLock);
                }
                finally
                {
                    if (isNotAlreadyLocked)
                    {
                        file.Unlock(userId);
                    }
                }
            }
        }
    }
}

