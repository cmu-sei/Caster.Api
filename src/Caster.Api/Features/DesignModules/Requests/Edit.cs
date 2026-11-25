// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Shared;
using FluentValidation;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Caster.Api.Domain.Models;
using Caster.Api.Data;
using Caster.Api.Domain.Services.ModuleScopes;

namespace Caster.Api.Features.DesignModules;

public class Edit
{
    [DataContract(Name = "EditDesignModuleCommand")]
    public record Command : DesignModuleFields, IRequest<DesignModule>
    {
        [JsonIgnore]
        public Guid DesignModuleId { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator(IValidationService validationService)
        {
            RuleFor(x => x.ModuleId).ModuleExists(validationService);
            RuleFor(x => x.Name)
                .NotEmpty()
                .Matches(DesignModuleValidation.TerraformNamePattern)
                .WithMessage(DesignModuleValidation.TerraformNameMessage);
            RuleFor(x => x.ModuleVersion).NotEmpty();
        }
    }

    public class Handler(
        ICasterAuthorizationService authorizationService,
        CasterContext dbContext,
        IModuleAccessService moduleAccess) : BaseHandler<Command, DesignModule>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<Domain.Models.DesignModule>(request.DesignModuleId, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

        public override async Task<DesignModule> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var designModule = await dbContext.DesignModules
                .Where(x => x.Id == request.DesignModuleId)
                .SingleOrDefaultAsync(cancellationToken);

            if (designModule == null)
                throw new EntityNotFoundException<DesignModule>();

            // Switching to another Module is checked against the project's scopes; keeping the
            // Module a Design already uses is not, so it keeps working after the scopes change.
            if (request.ModuleId != designModule.ModuleId)
                await DesignModuleValidation.EnsureModuleAllowed(dbContext, moduleAccess, designModule.DesignId, request.ModuleId, cancellationToken);

            request.ApplyTo(designModule);
            await DesignModuleValidation.EnsureComplete(dbContext, designModule, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            return designModule.ToDesignModule();
        }
    }
}
