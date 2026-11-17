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
using Caster.Api.Domain.Services.Modules;
using Caster.Api.Features.Designs;
using Caster.Api.Features.Shared;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Infrastructure.Options;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.Files;

public class DraftWithAi
{
    [DataContract(Name = "DraftTerraformWithAiCommand")]
    public record Command : IRequest<AiTerraformDraft>
    {
        [JsonIgnore]
        public Guid DirectoryId { get; set; }

        /// <summary>
        /// What the Terraform should do, in the designer's words.
        /// </summary>
        [DataMember]
        public string Prompt { get; init; }

        /// <summary>
        /// The Directory's file the designer has open, which the draft changes first. Empty to draft for the whole Directory.
        /// </summary>
        [DataMember]
        public Guid? FileId { get; init; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator(IValidationService validationService)
        {
            RuleFor(x => x.DirectoryId).DirectoryExists(validationService);
            RuleFor(x => x.Prompt).NotEmpty().MaximumLength(8000);
        }
    }

    public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext, IAiModelClient aiModelClient, AiOptions aiOptions, TerraformOptions terraformOptions) : BaseHandler<Command, AiTerraformDraft>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<Directory>(request.DirectoryId, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

        public override async Task<AiTerraformDraft> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            if (!aiOptions.Enabled)
                throw new ConflictException("AI drafting is not enabled on this server.");

            var files = await LoadContextFiles(request.DirectoryId, cancellationToken);
            AiContextFile focus = null;
            if (request.FileId.HasValue)
            {
                focus = files.SingleOrDefault(f => f.Id == request.FileId.Value && f.Editable)
                    ?? throw new ConflictException("The file to change must belong to this Directory, not a parent or a workspace.");
            }
            if (files.Sum(f => f.Content?.Length ?? 0) > AiTerraformDraftBuilder.MaxContextCharacters)
                throw new ConflictException("This Directory and its parents hold too much Terraform to send in one draft. Draft from a smaller Directory.");

            var catalog = await LoadCatalog(cancellationToken);
            var input = await aiModelClient.InvokeToolAsync(
                AiTerraformDraftBuilder.BuildSystemPrompt(catalog),
                AiTerraformDraftBuilder.BuildUserPrompt(request.Prompt, files, await LoadDesigns(request.DirectoryId, cancellationToken), focus),
                AiTerraformDraftBuilder.ToolName,
                AiTerraformDraftBuilder.ToolDescription,
                AiTerraformDraftBuilder.BuildToolSchema(),
                cancellationToken);

            return AiTerraformDraftBuilder.Review(input, files);
        }

        // what a workspace of the Directory is built from: its own files and every parent's, but no workspace's
        private async Task<List<AiContextFile>> LoadContextFiles(Guid directoryId, CancellationToken cancellationToken)
        {
            var directory = await dbContext.Directories.AsNoTracking().SingleAsync(d => d.Id == directoryId, cancellationToken);
            var pathIds = directory.PathIds();
            return await dbContext.Files
                .AsNoTracking()
                .Where(f => pathIds.Contains(f.DirectoryId) && f.WorkspaceId == null)
                .Select(f => new AiContextFile(f.Id, f.Name, f.Content, f.Directory.Name, f.DirectoryId == directoryId))
                .ToListAsync(cancellationToken);
        }

        // the modules Terraform runs can download, as the Design draft offers them, for every hypervisor
        private async Task<List<AiCatalogModule>> LoadCatalog(CancellationToken cancellationToken)
        {
            var modules = await dbContext.Modules
                .Include(m => m.Versions)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            // Modules that match no configured source came from the legacy Gitlab group.
            var fallbackSource = terraformOptions.GitlabGroupId.HasValue ? ModuleRepositoryDispatcher.GitlabSourceName : null;
            return AiDesignDraftBuilder.SelectCatalog(modules, terraformOptions.ModuleSources, fallbackSource, hypervisors: null);
        }

        private async Task<List<string>> LoadDesigns(Guid directoryId, CancellationToken cancellationToken)
        {
            var directory = await dbContext.Directories.AsNoTracking().SingleAsync(d => d.Id == directoryId, cancellationToken);
            var pathIds = directory.PathIds();
            var designs = await dbContext.Designs
                .AsNoTracking()
                .Include(d => d.Modules).ThenInclude(m => m.Module)
                .Where(d => d.Enabled && pathIds.Contains(d.DirectoryId))
                .OrderBy(d => d.Name)
                .ToListAsync(cancellationToken);
            return designs
                .Select(d => $"{d.Name}: " + string.Join(", ", d.Modules.Where(m => m.Enabled).Select(m => $"module \"{m.Name}\" ({m.Module.Name})")))
                .ToList();
        }
    }
}
