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
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Workspaces.Interfaces;
using FluentValidation;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Features.Shared.Validators;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Features.Shared;

namespace Caster.Api.Features.Workspaces
{
    public class Edit
    {
        [DataContract(Name = "EditWorkspaceCommand")]
        public class Command : WorkspaceFields, IRequest<Workspace>
        {
            public Guid Id { get; set; }
        }

        public class CommandValidator : AbstractValidator<Command>
        {
            public CommandValidator(TerraformOptions options)
            {
                RuleFor(x => x.Parallelism.Value)
                    .ParalellismValidation(options)
                    .When(x => x.Parallelism.HasValue);
                RuleFor(x => x.AzureDestroyFailureThreshold.Value)
                    .AzureThresholdValidation()
                    .When(x => x.AzureDestroyFailureThreshold.HasValue);
            }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Command, Workspace>
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Domain.Models.Workspace>(request.Id, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

            public override async Task<Workspace> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var workspace = await dbContext.Workspaces.FindAsync([request.Id], cancellationToken);

                if (workspace == null)
                    throw new EntityNotFoundException<Workspace>();

                mapper.Map(request, workspace);

                await dbContext.SaveChangesAsync(cancellationToken);
                return mapper.Map<Workspace>(workspace);
            }
        }
    }
}
