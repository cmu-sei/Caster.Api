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
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;

namespace Caster.Api.Features.Files
{
    public class AdminUnlock
    {
        [DataContract(Name="AdministrativelyUnlockFileCommand")]
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
                file.AdministrativelyUnlock((await _identityResolver.IsAdminAsync()));
            }

            protected override async Task Authorize()
            {
                if (!(await _authorizationService.AuthorizeAsync(
                    _user, null, new FullRightsRequirement())).Succeeded)
                {
                    throw new ForbiddenException();
                }
            }
        }
    }
}

