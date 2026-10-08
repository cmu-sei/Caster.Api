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
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Domain.Services;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Features.Files.Interfaces;
using System.Text.Json.Serialization;
using FluentValidation;
using Caster.Api.Features.Shared.Validators;

namespace Caster.Api.Features.Files
{
    public class Edit
    {
        [DataContract(Name = "EditFileCommand")]
        public class Command : FileFields, IRequest<File>, IFileCommand
        {
            [JsonIgnore]
            public Guid Id { get; set; }
        }

        public class CommandValidator : AbstractValidator<Command>
        {
            public CommandValidator()
            {
                RuleFor(x => x.Name).FileNameValidation();
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

