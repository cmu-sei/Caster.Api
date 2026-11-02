// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
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

namespace Caster.Api.Features.AiAssist;

public class Ask
{
    [DataContract(Name = "AskAiAssistantCommand")]
    public record Command : IRequest<AiAssistResult>
    {
        [JsonIgnore]
        public Guid DirectoryId { get; set; }

        /// <summary>
        /// What the planner asks for, in their words.
        /// </summary>
        [DataMember]
        public string Prompt { get; init; }

        /// <summary>
        /// The workspace the planner is working in, whose latest run is shown. Empty for every workspace of the Directory.
        /// </summary>
        [DataMember]
        public Guid? WorkspaceId { get; init; }

        /// <summary>
        /// The conversation so far, oldest first; the latest messages are resent to the model.
        /// </summary>
        [DataMember]
        public List<AiAssistMessage> History { get; init; } = [];
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator(IValidationService validationService)
        {
            RuleFor(x => x.DirectoryId).DirectoryExists(validationService);
            RuleFor(x => x.Prompt).NotEmpty().MaximumLength(8000);
            RuleFor(x => x.History).Must(h => h == null || h.Count <= 200).WithMessage("The conversation is too long. Start a new one.");
        }
    }

    public class Handler(
        ICasterAuthorizationService authorizationService,
        CasterContext dbContext,
        IAiModelClient aiModelClient,
        AiOptions aiOptions,
        TerraformOptions terraformOptions,
        ITerraformService terraformService,
        IAiTaskContext aiTask) : BaseHandler<Command, AiAssistResult>
    {
        // whoever can change the Directory's files may ask; nobody else reaches the model
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<Directory>(request.DirectoryId, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

        public override async Task<AiAssistResult> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            if (!aiOptions.Enabled)
                throw new ConflictException("AI drafting is not enabled on this server.");
            aiTask.Use(AiTasks.Assist, request.DirectoryId);

            var state = await AiAssistState.LoadAsync(dbContext, terraformService, terraformOptions, request.DirectoryId, request.WorkspaceId, cancellationToken);
            var input = await aiModelClient.InvokeToolAsync(
                AiAssistBuilder.SystemPrompt,
                AiAssistBuilder.BuildUserPrompt(request.Prompt, request.History, state),
                AiAssistBuilder.ToolName,
                AiAssistBuilder.ToolDescription,
                AiAssistBuilder.BuildSchema(state),
                cancellationToken);

            var result = AiAssistBuilder.Read(input, state);
            if (state.Trimmed.Count > 0)
                result.Warnings.Add("The directory's files were too large to show the model in full; the largest were cut and cannot be changed here.");
            return result;
        }
    }
}
