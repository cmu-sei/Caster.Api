// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Features.DesignModules;
using Caster.Api.Features.Shared;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Extensions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.Designs;

public class CreateFromAiDraft
{
    [DataContract(Name = "CreateDesignFromAiDraftCommand")]
    public record Command : IRequest<Design>
    {
        [JsonIgnore]
        public Guid DirectoryId { get; set; }

        /// <summary>
        /// Name of the design.
        /// </summary>
        [DataMember]
        public string Name { get; init; }

        /// <summary>
        /// The reviewed modules of the draft.
        /// </summary>
        [DataMember]
        public DesignModuleFields[] Modules { get; init; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator(IValidationService validationService)
        {
            RuleFor(x => x.DirectoryId).DirectoryExists(validationService);
            RuleFor(x => x.Name).NotEmpty();
            RuleFor(x => x.Modules).NotEmpty();
            RuleFor(x => x.Modules)
                .Must(m => m == null || m.Select(x => x.Name).Distinct().Count() == m.Length)
                .WithMessage("Each module in the design needs a unique name");
            RuleForEach(x => x.Modules).ChildRules(module =>
            {
                module.RuleFor(x => x.ModuleId).ModuleExists(validationService);
                module.RuleFor(x => x.Name)
                    .NotEmpty()
                    .Matches(DesignModuleValidation.TerraformNamePattern)
                    .WithMessage(DesignModuleValidation.TerraformNameMessage);
                module.RuleFor(x => x.ModuleVersion).NotEmpty();
            });
        }
    }

    public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Command, Design>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<Directory>(request.DirectoryId, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

        /// <summary>
        /// The Design is created disabled, so it is not written into any Workspace until a
        /// planner enables it, which it cannot while a value is a placeholder. Each module must
        /// still be complete, as a hand-built one must.
        /// </summary>
        public override async Task<Design> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var design = new Domain.Models.Design
            {
                Name = request.Name,
                DirectoryId = request.DirectoryId,
                Enabled = false
            };
            dbContext.Designs.Add(design);

            var built = new List<(Domain.Models.DesignModule Module, ModuleVersion Version)>();
            for (var i = 0; i < request.Modules.Length; i++)
            {
                var fields = request.Modules[i];
                var version = await dbContext.ModuleVersions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(v => v.ModuleId == fields.ModuleId && v.Name == fields.ModuleVersion, cancellationToken);

                // A required value the planner has not got yet is saved as a placeholder,
                // which keeps the Design from being enabled until it is replaced.
                var values = DesignModules.ModuleValue.ToDomain(fields.Values ?? []);
                if (version != null)
                    AiDesignDraftBuilder.FillMissingRequired(version, values);

                var designModule = new Domain.Models.DesignModule
                {
                    DesignId = design.Id,
                    ModuleId = fields.ModuleId,
                    Name = fields.Name,
                    ModuleVersion = fields.ModuleVersion,
                    Values = values
                };

                // name, version and required variables, as DesignModules.Create checks them
                await DesignModuleValidation.EnsureComplete(dbContext, designModule, cancellationToken);
                var unknown = designModule.Values
                    .Select(v => v.Name)
                    .Where(n => !version.Variables.Any(v => v.Name == n))
                    .ToArray();
                if (unknown.Length > 0)
                {
                    throw new Infrastructure.Exceptions.ValidationException(new Dictionary<string, string[]>
                    {
                        [$"Modules[{i}].Values"] = [$"Module version '{version.Name}' has no variables named {string.Join(", ", unknown)}"]
                    });
                }

                built.Add((designModule, version));
            }

            var referenceProblems = AiDesignDraftBuilder.ReferenceProblems(
                built.Select(b => (b.Module.Name, b.Version, (IEnumerable<Domain.Models.ModuleValue>)b.Module.Values)));
            if (referenceProblems.Count > 0)
            {
                throw new Infrastructure.Exceptions.ValidationException(new Dictionary<string, string[]>
                {
                    [nameof(Command.Modules)] = referenceProblems.ToArray()
                });
            }

            foreach (var (designModule, _) in built)
                design.Modules.Add(designModule);

            await dbContext.SaveChangesAsync(cancellationToken);
            return mapper.Map<Design>(design);
        }
    }
}
