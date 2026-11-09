// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.EnvironmentSets;
using Caster.Api.Domain.Services.Inventory;
using Caster.Api.Features.Shared;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using EntryEntity = Caster.Api.Domain.Models.EnvironmentSetEntry;

namespace Caster.Api.Features.EnvironmentSets;

/// <summary>What a multi-edit change does to each selected set.</summary>
public enum EnvironmentSetEntryChangeOperation
{
    /// <summary>Changes the entry in the selected sets that have it.</summary>
    Update,

    /// <summary>Changes the entry in the selected sets that have it, and adds it to those that don't.</summary>
    Upsert,

    /// <summary>Removes the entry from every selected set that has it.</summary>
    Delete
}

/// <summary>
/// Edits several sets at once: each change names an entry by kind and target name, which identify
/// it, so a change can alter its value and source but not its name.
/// </summary>
public class BatchEdit
{
    [DataContract(Name = "BatchEditEnvironmentSetsCommand")]
    public class Command : IRequest<EnvironmentSet[]>
    {
        /// <summary>The sets to change.</summary>
        [DataMember]
        public Guid[] EnvironmentSetIds { get; set; } = [];

        /// <summary>The changes, at most one per kind and target name.</summary>
        [DataMember]
        public EntryChange[] Changes { get; set; } = [];
    }

    [DataContract(Name = "EnvironmentSetEntryChange")]
    public class EntryChange : IEnvironmentSetEntryFields
    {
        [DataMember]
        public EnvironmentSetEntryChangeOperation Operation { get; set; }

        [DataMember]
        public EnvironmentSetEntryKind Kind { get; set; }

        [DataMember]
        public string TargetName { get; set; }

        /// <summary>For an update or upsert: the entry's new source and value, as for a single entry.</summary>
        [DataMember]
        public EnvironmentSetValueSource ValueSource { get; set; }

        [DataMember]
        public string SecretName { get; set; }

        [DataMember]
        public string Value { get; set; }

        [DataMember]
        public string ConnectionName { get; set; }

        [DataMember]
        public ConnectionField? ConnectionField { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator(ISecretSource secretSource, IInfrastructureConnectionCatalog connections)
        {
            RuleFor(x => x.EnvironmentSetIds)
                .NotEmpty()
                .Must(x => x.Distinct().Count() == x.Length)
                .WithMessage("A set is named more than once.");

            RuleFor(x => x.Changes)
                .NotEmpty()
                .Must(x => x.Select(c => EntryEntity.GetEffectiveName(c.Kind, c.TargetName)).Distinct(StringComparer.Ordinal).Count() == x.Length)
                .WithMessage("An entry is changed more than once.");

            var fields = new EnvironmentSetEntryFieldsValidator<EntryChange>(secretSource, connections);

            RuleForEach(x => x.Changes).ChildRules(change =>
            {
                change.RuleFor(x => x.Operation).IsInEnum();
                change.RuleFor(x => x.Kind).IsInEnum();
                change.RuleFor(x => x.TargetName).NotEmpty();
                change.When(x => x.Operation != EnvironmentSetEntryChangeOperation.Delete, () => change.RuleFor(x => x).SetValidator(fields));
            });
        }
    }

    public class Handler(
        ICasterAuthorizationService authorizationService,
        CasterContext dbContext,
        IEnvironmentSetViews views) : BaseHandler<Command, EnvironmentSet[]>
    {
        /// <summary>
        /// A change that makes, edits or removes a connection entry also needs
        /// ManageInfrastructure, as a single entry does.
        /// </summary>
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken)
        {
            if (!await authorizationService.Authorize([SystemPermission.ManageEnvironmentSets], cancellationToken))
                return false;

            var connection = request.Changes.Any(x =>
                x.Operation != EnvironmentSetEntryChangeOperation.Delete && x.ValueSource == EnvironmentSetValueSource.Connection);

            if (!connection)
            {
                var names = request.Changes.Select(x => EntryEntity.GetEffectiveName(x.Kind, x.TargetName)).ToHashSet(StringComparer.Ordinal);
                var stored = await dbContext.EnvironmentSetEntries
                    .Where(x => request.EnvironmentSetIds.Contains(x.EnvironmentSetId) && x.ValueSource == EnvironmentSetValueSource.Connection)
                    .Select(x => new { x.Kind, x.TargetName })
                    .ToListAsync(cancellationToken);

                connection = stored.Any(x => names.Contains(EntryEntity.GetEffectiveName(x.Kind, x.TargetName)));
            }

            return !connection || await authorizationService.Authorize([SystemPermission.ManageInfrastructure], cancellationToken);
        }

        public override Task<EnvironmentSet[]> HandleRequest(Command request, CancellationToken cancellationToken) =>
            dbContext.WithEnvironmentSetLock(() => Handle(request, cancellationToken), cancellationToken);

        private async Task<EnvironmentSet[]> Handle(Command request, CancellationToken cancellationToken)
        {
            var ids = request.EnvironmentSetIds.Distinct().ToList();

            var sets = await dbContext.EnvironmentSets
                .Include(x => x.Entries)
                .Where(x => ids.Contains(x.Id))
                .ToListAsync(cancellationToken);

            if (sets.Count != ids.Count)
            {
                throw new EntityNotFoundException<EnvironmentSet>();
            }

            // The whole proposed final state is checked before anything changes.
            var final = new Dictionary<Guid, (string Name, List<EntryEntity> Entries)>();
            var removals = new List<EntryEntity>();
            var updates = new List<(EntryEntity Entry, EntryChange Change)>();
            var additions = new List<EntryEntity>();

            foreach (var set in sets)
            {
                var entries = set.Entries.ToList();

                foreach (var change in request.Changes)
                {
                    var name = EntryEntity.GetEffectiveName(change.Kind, change.TargetName);
                    var existing = entries.FirstOrDefault(x => x.EffectiveName == name);

                    switch (change.Operation)
                    {
                        case EnvironmentSetEntryChangeOperation.Delete when existing is not null:
                            entries.Remove(existing);
                            removals.Add(existing);
                            break;

                        case EnvironmentSetEntryChangeOperation.Update or EnvironmentSetEntryChangeOperation.Upsert when existing is not null:
                            var candidate = new EntryEntity { Id = existing.Id, EnvironmentSetId = set.Id };
                            candidate.Apply(change);
                            entries[entries.IndexOf(existing)] = candidate;
                            updates.Add((existing, change));
                            break;

                        case EnvironmentSetEntryChangeOperation.Upsert:
                            var added = new EntryEntity { EnvironmentSetId = set.Id };
                            added.Apply(change);
                            entries.Add(added);
                            additions.Add(added);
                            break;
                    }
                }

                final[set.Id] = (set.Name, entries);
            }

            await dbContext.EnsureNoConflictsInFinalState(final, cancellationToken);

            dbContext.EnvironmentSetEntries.RemoveRange(removals);

            foreach (var (entry, change) in updates)
            {
                entry.Apply(change);
            }

            dbContext.EnvironmentSetEntries.AddRange(additions);
            await dbContext.SaveChangesAsync(cancellationToken);

            return await views.GetSets(x => ids.Contains(x.Id), cancellationToken);
        }
    }
}
