// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using AutoMapper;
using System.Runtime.Serialization;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Domain.Services;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Features.Files.Interfaces;
using CodeAnalysis = Microsoft.CodeAnalysis;
using System.Text.Json.Serialization;
using Caster.Api.Infrastructure.Authorization;
using FluentValidation;
using Caster.Api.Features.Shared.Services;

namespace Caster.Api.Features.Files
{
    public class PartialEdit
    {
        [DataContract(Name = "PartialEditFileCommand")]
        public class Command : FileUpdateRequest, IRequest<File>, IFileCommand
        {
            [JsonIgnore]
            public Guid Id { get; set; }

            /// <summary>
            /// Name of the file.
            /// </summary>
            [DataMember]
            public string Name { get; set; }

            /// <summary>
            /// ID of the directory this file is under.
            /// </summary>
            [DataMember]
            public Guid? DirectoryId { get; set; }


            /// <summary>
            /// An optional Workspace to assign this File to.
            /// </summary>
            [DataMember]
            public CodeAnalysis.Optional<Guid?> WorkspaceId { get; set; }

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
                RuleFor(x => x.DirectoryId.Value).DirectoryExists(validationService).When(x => x.DirectoryId.HasValue);
                RuleFor(x => x.WorkspaceId.Value.Value).WorkspaceExists(validationService).When(x => x.WorkspaceId.HasValue && x.WorkspaceId.Value.HasValue);
            }
        }

        public class Handler(
            CasterContext dbContext,
            ILockService lockService,
            IGetFileQuery fileQuery,
            ICasterAuthorizationService authorizationService,
            IIdentityResolver identityResolver,
            IMapper mapper) : FileCommandHandler<Command, File>(dbContext, lockService, fileQuery, authorizationService)
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
                file = mapper.Map(_request, file);
                file.Save(identityResolver.GetId(), canLock: await CanLock(file.Id, cancellationToken));
            }
        }
    }
}

