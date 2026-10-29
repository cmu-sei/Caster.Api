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
using Caster.Api.Domain.Services.Inventory;
using Caster.Api.Domain.Services.Modules;
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

        /// <summary>
        /// Hypervisors the Design builds on: Proxmox, vSphere or both. Empty means every
        /// hypervisor configured for Terraform runs.
        /// </summary>
        [DataMember]
        public string[] Hypervisors { get; init; } = [];
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator(IValidationService validationService)
        {
            RuleFor(x => x.DirectoryId).DirectoryExists(validationService);
            RuleFor(x => x.Prompt).NotEmpty().MaximumLength(8000);
            RuleForEach(x => x.Hypervisors)
                .Must(h => Domain.Models.Hypervisors.All.Contains(h?.Trim(), StringComparer.OrdinalIgnoreCase))
                .WithMessage($"Hypervisor must be one of {string.Join(", ", Domain.Models.Hypervisors.All)}.");
        }
    }

    public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext, IAiModelClient aiModelClient, AiOptions aiOptions, TerraformOptions terraformOptions, IInventoryService inventoryService) : BaseHandler<Command, AiDesignDraft>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<Directory>(request.DirectoryId, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

        public override async Task<AiDesignDraft> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            if (!aiOptions.Enabled)
                throw new ConflictException("AI drafting is not enabled on this server.");

            var warnings = new List<string>();
            var hypervisors = AiDesignDraftBuilder.ResolveHypervisors(
                Domain.Models.Hypervisors.Configured(terraformOptions), request.Hypervisors, warnings);

            var catalog = await LoadCatalog(hypervisors, cancellationToken);
            if (catalog.Count == 0)
                throw new ConflictException(hypervisors == null
                    ? "There are no module versions that Terraform runs can download. Import modules first."
                    : $"There are no module versions for {string.Join(" or ", hypervisors)} that Terraform runs can download.");

            var inventory = await AiInventory.ForCaller(authorizationService, inventoryService, hypervisors, cancellationToken);
            var input = await aiModelClient.InvokeToolAsync(
                AiDesignDraftBuilder.BuildSystemPrompt(catalog, hypervisors, inventory.BuildPromptSection()),
                AiDesignDraftBuilder.BuildUserPrompt(request.Prompt, request.DesignName),
                AiDesignDraftBuilder.ToolName,
                AiDesignDraftBuilder.ToolDescription,
                AiDesignDraftBuilder.BuildToolSchema(catalog),
                cancellationToken);

            var draft = AiDesignDraftBuilder.Review(input, catalog, request.DesignName, inventory, hypervisors);
            draft.Warnings.InsertRange(0, warnings);
            if (inventory.Grounding.Connections.Any(c => c.Truncated))
                draft.Warnings.Add("The inventory was too large to show the model in full, so it may not have seen every template or network.");
            return draft;
        }

        // The model is offered the highest version of each module, as the Design editor defaults to.
        private async Task<List<AiCatalogModule>> LoadCatalog(string[] hypervisors, CancellationToken cancellationToken)
        {
            var modules = await dbContext.Modules
                .Include(m => m.Versions)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            // Modules that match no configured source came from the legacy Gitlab group.
            var fallbackSource = terraformOptions.GitlabGroupId.HasValue ? ModuleRepositoryDispatcher.GitlabSourceName : null;
            return AiDesignDraftBuilder.SelectCatalog(modules, terraformOptions.ModuleSources, fallbackSource, hypervisors);
        }
    }
}
