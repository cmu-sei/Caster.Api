// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Domain.Services.Inventory;
using Caster.Api.Features.Designs;
using Caster.Api.Features.Shared;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Infrastructure.Options;
using FluentValidation;
using MediatR;

namespace Caster.Api.Features.Files;

public class DraftWithAiFromBrief
{
    [DataContract(Name = "DraftTerraformFromBriefCommand")]
    public record Command : IRequest<AiTerraformDraft>
    {
        [JsonIgnore]
        public Guid DirectoryId { get; set; }

        /// <summary>
        /// The scenario the range is for.
        /// </summary>
        [DataMember]
        public AiScenarioBrief Brief { get; init; }

        /// <summary>
        /// What else the planner wants, in their words, e.g. which parent directory's machines to reuse.
        /// </summary>
        [DataMember]
        public string Instructions { get; init; }

        /// <summary>
        /// Hypervisors the Terraform builds on: Proxmox, vSphere or both. Empty means the ones the Directory's files
        /// already use, or else every hypervisor configured for Terraform runs.
        /// </summary>
        [DataMember]
        public string[] Hypervisors { get; init; } = [];
    }

    public class Validator : AbstractValidator<Command>
    {
        public const int MaxName = 200;
        public const int MaxText = 4000;

        public Validator(IValidationService validationService)
        {
            RuleFor(x => x.DirectoryId).DirectoryExists(validationService);
            RuleFor(x => x.Instructions).MaximumLength(8000);
            RuleForEach(x => x.Hypervisors)
                .Must(h => Domain.Models.Hypervisors.All.Contains(h?.Trim(), StringComparer.OrdinalIgnoreCase))
                .WithMessage($"Hypervisor must be one of {string.Join(", ", Domain.Models.Hypervisors.All)}.");

            RuleFor(x => x.Brief).NotNull();
            When(x => x.Brief != null, () =>
            {
                RuleFor(x => x.Brief.Title).NotEmpty().MaximumLength(MaxName);
                RuleFor(x => x.Brief.Summary).MaximumLength(MaxText);
                RuleFor(x => x.Brief.Organizations).Must(l => l == null || l.Count <= 20).WithMessage("A brief may have at most 20 organizations.");
                RuleFor(x => x.Brief.Segments).Must(l => l == null || l.Count <= 60).WithMessage("A brief may have at most 60 segments.");
                RuleFor(x => x.Brief.Systems).Must(l => l == null || l.Count <= 200).WithMessage("A brief may have at most 200 systems.");
                RuleFor(x => x.Brief.Teams).Must(l => l == null || l.Count <= 40).WithMessage("A brief may have at most 40 teams.");
                RuleFor(x => x.Brief.Objectives).Must(l => l == null || l.Count <= 40).WithMessage("A brief may have at most 40 objectives.");
                RuleFor(x => x.Brief.Constraints).Must(l => l == null || l.Count <= 40).WithMessage("A brief may have at most 40 constraints.");
                RuleForEach(x => x.Brief.Objectives).MaximumLength(MaxText);
                RuleForEach(x => x.Brief.Constraints).MaximumLength(MaxText);
                RuleForEach(x => x.Brief.Organizations).ChildRules(o =>
                {
                    o.RuleFor(x => x.Name).NotEmpty().MaximumLength(MaxName);
                    o.RuleFor(x => x.Description).MaximumLength(MaxText);
                    o.RuleFor(x => x.Sites).Must(l => l == null || l.Count <= 20).WithMessage("An organization may have at most 20 sites.");
                    o.RuleForEach(x => x.Sites).ChildRules(s =>
                    {
                        s.RuleFor(x => x.Name).NotEmpty().MaximumLength(MaxName);
                        s.RuleFor(x => x.Description).MaximumLength(MaxText);
                    });
                });
                RuleForEach(x => x.Brief.Segments).ChildRules(s =>
                {
                    s.RuleFor(x => x.Name).NotEmpty().MaximumLength(MaxName);
                    s.RuleFor(x => x.Description).MaximumLength(MaxText);
                    s.RuleFor(x => x.Cidr).Must(c => string.IsNullOrWhiteSpace(c) || Ipv4Network.TryParse(c, out _, out _))
                        .WithMessage("Cidr must be an IPv4 network such as 10.20.30.0/24.");
                });
                RuleForEach(x => x.Brief.Systems).ChildRules(s =>
                {
                    s.RuleFor(x => x.Name).NotEmpty().MaximumLength(MaxName);
                    s.RuleFor(x => x.Description).MaximumLength(MaxText);
                    s.RuleFor(x => x.Services).Must(l => l == null || l.Count <= 20).WithMessage("A system may list at most 20 services.");
                });
                RuleFor(x => x.Brief.Attacker.Path).Must(l => l == null || l.Count <= 40).WithMessage("An attack path may have at most 40 steps.")
                    .When(x => x.Brief.Attacker != null);
                RuleFor(x => x.Brief.Attacker.Description).MaximumLength(MaxText).When(x => x.Brief.Attacker != null);
                RuleForEach(x => x.Brief.Attacker.Path).ChildRules(p =>
                {
                    p.RuleFor(x => x.From).NotEmpty().MaximumLength(MaxName);
                    p.RuleFor(x => x.To).NotEmpty().MaximumLength(MaxName);
                    p.RuleFor(x => x.Description).MaximumLength(MaxText);
                }).When(x => x.Brief.Attacker != null);
                RuleForEach(x => x.Brief.Teams).ChildRules(t =>
                {
                    t.RuleFor(x => x.Name).NotEmpty().MaximumLength(MaxName);
                    t.RuleFor(x => x.Workstations).InclusiveBetween(0, 200);
                    t.RuleFor(x => x.Description).MaximumLength(MaxText);
                });
            });
        }
    }

    public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext, IAiModelClient aiModelClient, AiOptions aiOptions, TerraformOptions terraformOptions, IInventoryService inventoryService, IAiTaskContext aiTask) : BaseHandler<Command, AiTerraformDraft>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<Directory>(request.DirectoryId, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

        public override Task<AiTerraformDraft> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            aiTask.Use(AiTasks.Brief, request.DirectoryId);
            return new DraftWithAi.Drafter(authorizationService, dbContext, aiModelClient, aiOptions, terraformOptions, inventoryService)
                .Draft(
                    request.DirectoryId,
                    AiScenarioBriefPrompt.Build(request.Brief, request.Instructions),
                    fileId: null,
                    request.Hypervisors,
                    AiScenarioBriefPrompt.Expectations(request.Brief),
                    cancellationToken);
        }
    }
}
