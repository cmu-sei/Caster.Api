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
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Domain.Services;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Features.Files.Interfaces;

namespace Caster.Api.Features.Files
{
    public class Unlock
    {
        [DataContract(Name="UnlockFileCommand")]
        public class Command : FileMetadataUpdateRequest, IRequest<File>, IFileCommand
        {
            public Guid Id { get; set; }
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
                return await base.Handle(request, cancellationToken);
            }

            protected override Task PerformOperation(Domain.Models.File file)
            {
                file.Unlock(_user.GetId());
                return Task.CompletedTask;
            }
        }
    }
}

