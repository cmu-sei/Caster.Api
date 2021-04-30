// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using AutoMapper;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Domain.Models;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Domain.Services;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Features.Files.Interfaces;
using System.Text.Json.Serialization;

namespace Caster.Api.Features.Files
{
    public class Rename
    {
        [DataContract(Name="RenameFileCommand")]
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

        public class Handler : FileCommandHandler, IRequestHandler<Command, File>
        {
            private Command _request { get; set; }

            public Handler(
                CasterContext db,
                IMapper mapper,
                IAuthorizationService authorizationService,
                IIdentityResolver identityResolver,
                ILockService lockService,
                IGetFileQuery fileQuery)
                : base(db, mapper, authorizationService, identityResolver, lockService, fileQuery) {}

            public async Task<File> Handle(Command request, CancellationToken cancellationToken)
            {
                _request = request;
                return await base.Handle(request, cancellationToken);
            }

            protected override async Task PerformOperation(Domain.Models.File file)
            {
                var isAdmin = await _identityResolver.IsAdminAsync();
                var userId = _user.GetId();
                var isNotAlreadyLocked = (userId != file.LockedById);

                if(isNotAlreadyLocked)
                {
                    try
                    {
                        file.Lock(userId, isAdmin);
                    }
                    catch (FileConflictException) 
                    {
                        throw new FileConflictException ("Cannot rename a file while it's being edited or locked by another user.");
                    }
                }

                try
                {
                    file.Name = _request.Name;
                    file.Save(userId, isAdmin);
                }
                finally
                {
                    if(isNotAlreadyLocked)
                    {
                        file.Unlock(userId);
                    }
                }
            }
        }
    }
}

