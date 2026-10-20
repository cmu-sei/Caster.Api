// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Features.Shared;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Infrastructure.Options;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.Designs;

public class DraftWithAi
{
    [DataContract(Name = "DraftDesignWithAiCommand")]
    public record Command : IRequest<AiDesignDraft>
    {
        [JsonIgnore]
        public Guid DirectoryId { get; set; }

        /// <summary>
        /// What the topology should contain, in the planner's words.
        /// </summary>
        [DataMember]
        public string Prompt { get; init; }

        /// <summary>
        /// Name for the Design. The model picks one if this is empty.
        /// </summary>
        [DataMember]
        public string DesignName { get; init; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator(IValidationService validationService)
        {
            RuleFor(x => x.DirectoryId).DirectoryExists(validationService);
            RuleFor(x => x.Prompt).NotEmpty().MaximumLength(8000);
        }
    }

    public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext, IAiModelClient aiModelClient, AiOptions aiOptions) : BaseHandler<Command, AiDesignDraft>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<Directory>(request.DirectoryId, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

        public override async Task<AiDesignDraft> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            if (!aiOptions.Enabled)
                throw new ConflictException("AI drafting is not enabled on this server.");

            var catalog = await LoadCatalog(cancellationToken);
            if (catalog.Count == 0)
                throw new ConflictException("There are no module versions to draft a design from. Import modules first.");

            var input = await aiModelClient.InvokeToolAsync(
                AiDesignDraftBuilder.BuildSystemPrompt(catalog),
                AiDesignDraftBuilder.BuildUserPrompt(request.Prompt, request.DesignName),
                AiDesignDraftBuilder.ToolName,
                AiDesignDraftBuilder.ToolDescription,
                AiDesignDraftBuilder.BuildToolSchema(catalog),
                cancellationToken);

            return AiDesignDraftBuilder.Review(input, catalog, request.DesignName);
        }

        // The model is offered the highest version of each module, as the Design editor defaults to.
        private async Task<List<AiCatalogModule>> LoadCatalog(CancellationToken cancellationToken)
        {
            var modules = await dbContext.Modules
                .Include(m => m.Versions)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            return modules
                .Where(m => m.Versions.Count > 0)
                .OrderBy(m => m.Name)
                .Select(m => new AiCatalogModule(m, ModuleVersionOrder.HighestFirst(m.Versions).First()))
                .ToList();
        }
    }
}
