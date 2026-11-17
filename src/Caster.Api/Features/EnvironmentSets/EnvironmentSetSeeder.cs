// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.EnvironmentSets;
using Caster.Api.Domain.Services.Inventory;
using Caster.Api.Infrastructure.Options;
using EnvironmentSetEntity = Caster.Api.Domain.Models.EnvironmentSet;
using EnvironmentSetEntryEntity = Caster.Api.Domain.Models.EnvironmentSetEntry;

namespace Caster.Api.Features.EnvironmentSets;

/// <summary>
/// Creates the Environment Sets named in <c>SeedData:EnvironmentSets</c>, with the same rules
/// the API applies. Every seed is checked before any is added, so a mistake adds nothing.
/// </summary>
public static class EnvironmentSetSeeder
{
    /// <returns>Why the seeds could not be applied; empty when they were.</returns>
    public static List<string> Seed(IEnumerable<SeedEnvironmentSet> seeds, CasterContext context, ISecretSource secretSource, IInfrastructureConnectionCatalog connections)
    {
        var sets = seeds?.Where(x => x is not null).ToList() ?? [];

        if (sets.Count == 0)
        {
            return [];
        }

        var errors = Validate(sets, secretSource, connections);

        if (errors.Count > 0)
        {
            return errors;
        }

        var existing = context.EnvironmentSets
            .Select(x => x.Name)
            .ToList()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var seed in sets.Where(x => !existing.Contains(x.Name.Trim())))
        {
            var set = new EnvironmentSetEntity
            {
                Name = seed.Name.Trim(),
                Description = seed.Description
            };

            foreach (var seedEntry in seed.Entries ?? [])
            {
                var entry = new EnvironmentSetEntryEntity();
                entry.Apply(seedEntry);
                set.Entries.Add(entry);
            }

            context.EnvironmentSets.Add(set);
        }

        context.SaveChanges();

        return [];
    }

    private static List<string> Validate(List<SeedEnvironmentSet> sets, ISecretSource secretSource, IInfrastructureConnectionCatalog connections)
    {
        var errors = new List<string>();
        var entryValidator = new EnvironmentSetEntryFieldsValidator<SeedEnvironmentSetEntry>(secretSource, connections);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < sets.Count; i++)
        {
            var seed = sets[i];
            var label = string.IsNullOrWhiteSpace(seed.Name)
                ? $"SeedData:EnvironmentSets:{i}"
                : $"SeedData:EnvironmentSets:{i} \"{seed.Name}\"";

            if (string.IsNullOrWhiteSpace(seed.Name))
            {
                errors.Add($"{label}: a Name is required.");
            }
            else if (seed.Name.Trim().Length > EnvironmentSetEntryFieldsValidator<SeedEnvironmentSetEntry>.MaximumNameLength)
            {
                errors.Add($"{label}: the Name is longer than {EnvironmentSetEntryFieldsValidator<SeedEnvironmentSetEntry>.MaximumNameLength} characters.");
            }
            else if (!names.Add(seed.Name.Trim()))
            {
                errors.Add($"{label}: another seeded Environment Set has this Name.");
            }

            var entries = seed.Entries?.Where(x => x is not null).ToList() ?? [];

            for (var j = 0; j < entries.Count; j++)
            {
                var entryLabel = $"{label}, Entries:{j} \"{entries[j].TargetName}\"";

                foreach (var failure in entryValidator.Validate(entries[j]).Errors)
                {
                    errors.Add($"{entryLabel}: {failure.ErrorMessage}");
                }
            }

            var effective = entries.Select(x => new EnvironmentSetEntryEntity { Kind = x.Kind, TargetName = x.TargetName });

            foreach (var duplicate in EnvironmentSetRules.GetDuplicateNames(effective))
            {
                errors.Add($"{label}: {duplicate} is set by more than one entry.");
            }
        }

        return errors;
    }
}
