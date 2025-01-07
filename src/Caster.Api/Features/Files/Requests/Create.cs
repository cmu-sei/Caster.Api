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

namespace Caster.Api.Features.Files
{
    public class Create
    {
        [DataContract(Name = "CreateFileCommand")]
        public class Command : FileUpdateRequest, IRequest<File>
        {
            /// <summary>
            /// Name of the file.
            /// </summary>
            [DataMember]
            public string Name { get; set; }

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

            /// <summary>
            /// The full contents of the file.
            /// </summary>
            [DataMember]
            public override string Content { get; set; }
        }

        public class CommandValidator : AbstractValidator<Command>
        {
            public CommandValidator(IValidationService validationService)
            {
                RuleFor(x => x.DirectoryId).DirectoryExists(validationService);
                RuleFor(x => x.WorkspaceId.Value).WorkspaceExists(validationService).When(x => x.WorkspaceId.HasValue);
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
