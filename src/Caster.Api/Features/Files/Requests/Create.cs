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
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Features.Files.Interfaces;
using FluentValidation;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Features.Shared;
using Caster.Api.Features.Shared.Validators;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace Caster.Api.Features.Files
{
    public class Create
    {
        [DataContract(Name = "CreateFileCommand")]
        public class Command : FileFields, IRequest<File>
        {
            /// <summary>
            /// ID of the directory this file is under.
            /// </summary>
            [DataMember]
            public Guid DirectoryId { get; set; }

            /// <summary>
            /// An optional Workspace to assign this File to
            /// </summary>
            [DataMember]
            public Guid? WorkspaceId { get; set; }

        }

        public class CommandValidator : AbstractValidator<Command>
        {
            public CommandValidator(IValidationService validationService)
            {
                RuleFor(x => x.Name).FileNameValidation();
                RuleFor(x => x.DirectoryId).DirectoryExists(validationService);
            }
        }

        public class Handler(
                CasterContext db,
                IMapper mapper,
                ICasterAuthorizationService authorizationService,
                IIdentityResolver identityResolver,
                IGetFileQuery fileQuery) : BaseHandler<Command, File>
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Directory>(request.DirectoryId, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

            public override async Task<File> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                if (request.WorkspaceId.HasValue)
                {
                    var workspaceDirectoryId = await db.Workspaces
                        .Where(w => w.Id == request.WorkspaceId.Value)
                        .Select(w => (Guid?)w.DirectoryId)
                        .SingleOrDefaultAsync(cancellationToken);

                    if (!workspaceDirectoryId.HasValue)
                        throw new EntityNotFoundException<Domain.Models.Workspace>();

                    if (workspaceDirectoryId.Value != request.DirectoryId)
                        throw new ConflictException("File and Workspace must be in the same Directory");
                }

                var file = mapper.Map<Domain.Models.File>(request);
                file.Save(
                    identityResolver.GetId(),
                    canLock: await authorizationService.Authorize<Directory>(request.DirectoryId, [SystemPermission.LockFiles], [ProjectPermission.LockFiles], cancellationToken),
                    bypassLock: true);

                db.Files.Add(file);
                await db.SaveChangesAsync(cancellationToken);

                return await fileQuery.ExecuteAsync(file.Id);
            }
        }
    }
}
