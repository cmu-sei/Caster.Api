// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.EnvironmentSets;
using Caster.Api.Features.Shared;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.EnvironmentSets;

/// <summary>
/// Copies a set with every entry, under a new name. A secret entry copies the secret's name,
/// never its value, and a connection entry its connection and field. The copy is assigned to
/// no project.
/// </summary>
public class Copy
{
    [DataContract(Name = "CopyEnvironmentSetCommand")]
    public class Command : IRequest<EnvironmentSet>
    {
        [JsonIgnore]
        public Guid Id { get; set; }

        /// <summary>The copy's name. Without one it is "&lt;name&gt; (copy)", numbered if that is taken.</summary>
        [DataMember]
        public string Name { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Name).MaximumLength(EnvironmentSetEntryFieldsValidator<IEnvironmentSetEntryFields>.MaximumNameLength);
        }
    }

    public class Handler(
        ICasterAuthorizationService authorizationService,
        CasterContext dbContext,
        IEntryValues entryValues,
        IEnvironmentSetViews views) : BaseHandler<Command, EnvironmentSet>
    {
        /// <summary>Copying connection entries creates them, so it also needs ManageInfrastructure.</summary>
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageEnvironmentSets], cancellationToken) &&
            (!await dbContext.EnvironmentSetEntries.AnyAsync(
                    x => x.EnvironmentSetId == request.Id && x.ValueSource == EnvironmentSetValueSource.Connection, cancellationToken) ||
                await authorizationService.Authorize([SystemPermission.ManageInfrastructure], cancellationToken));

        public override async Task<EnvironmentSet> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var set = await dbContext.EnvironmentSets
                .AsNoTracking()
                .Include(x => x.Entries)
                .SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
                ?? throw new EntityNotFoundException<EnvironmentSet>();

            var secretVariable = set.Entries.FirstOrDefault(x =>
                x.Kind == EnvironmentSetEntryKind.TerraformVariable && entryValues.IsSecret(x));

            if (secretVariable is not null)
            {
                throw new ConflictException(
                    $"The {EnvironmentSetRules.Describe(secretVariable)} is a secret, and a secret can't be a Terraform variable. Change it before copying the set.");
            }

            var name = string.IsNullOrWhiteSpace(request.Name)
                ? await FreeName($"{set.Name} (copy)", cancellationToken)
                : request.Name.Trim();

            await dbContext.EnsureUniqueSetName(name, null, cancellationToken);

            var copy = new Domain.Models.EnvironmentSet
            {
                Name = name,
                Description = set.Description
            };

            foreach (var entry in set.Entries)
            {
                copy.Entries.Add(new Domain.Models.EnvironmentSetEntry
                {
                    Kind = entry.Kind,
                    TargetName = entry.TargetName,
                    ValueSource = entry.ValueSource,
                    SecretName = entry.SecretName,
                    Value = entry.Value,
                    ConnectionName = entry.ConnectionName,
                    ConnectionField = entry.ConnectionField
                });
            }

            dbContext.EnvironmentSets.Add(copy);
            await dbContext.SaveChangesAsync(cancellationToken);

            return await views.GetSet(copy.Id, cancellationToken);
        }

        /// <summary><paramref name="name"/>, or "name 2", "name 3" and so on, whichever is free first.</summary>
        private async Task<string> FreeName(string name, CancellationToken cancellationToken)
        {
            var lowered = name.ToLower();
            var taken = (await dbContext.EnvironmentSets
                    .Where(x => x.Name.ToLower().StartsWith(lowered))
                    .Select(x => x.Name.ToLower())
                    .ToListAsync(cancellationToken))
                .ToHashSet();

            if (!taken.Contains(lowered))
                return name;

            for (var i = 2; ; i++)
            {
                var candidate = $"{name} {i}";

                if (!taken.Contains(candidate.ToLower()))
                    return candidate;
            }
        }
    }
}
