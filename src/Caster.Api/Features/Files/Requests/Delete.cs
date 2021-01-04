// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Features.Files.Interfaces;
using Caster.Api.Domain.Services;
using Caster.Api.Infrastructure.Extensions;

namespace Caster.Api.Features.Files
{
    public class Delete
    {
        [DataContract(Name="DeleteFileCommand")]
        public class Command : IRequest<File>, IFileDeleteRequest, IFileCommand
        {
            public Guid Id { get; set; }
        }

        public class Handler : FileCommandHandler, IRequestHandler<Command, File>
        {
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
                return await base.Handle(request, cancellationToken);
            }

            protected override async Task PerformOperation(Domain.Models.File file)
            {
                var isAdmin = await _identityResolver.IsAdminAsync();
                var userId = _user.GetId();

                file.Lock(userId, isAdmin);

                try
                {
                    file.Delete(isAdmin);
                }
                finally
                {
                    file.Unlock(userId);
                }
            }
        }
    }
}

