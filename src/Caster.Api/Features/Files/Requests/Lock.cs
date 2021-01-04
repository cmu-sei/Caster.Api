// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using AutoMapper;
using System.Runtime.Serialization;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Domain.Services;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Features.Files.Interfaces;

namespace Caster.Api.Features.Files
{
    public class Lock
    {
        [DataContract(Name="LockFileCommand")]
        public class Command : FileMetadataUpdateRequest, IRequest<File>, IFileCommand
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
                file.Lock(_user.GetId(), isAdmin: (await _identityResolver.IsAdminAsync()));
            }
        }
    }
}

