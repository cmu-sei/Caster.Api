// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Services.EnvironmentSets;
using Caster.Api.Infrastructure.Exceptions;
using Microsoft.EntityFrameworkCore;
using EntryEntity = Caster.Api.Domain.Models.EnvironmentSetEntry;

namespace Caster.Api.Features.EnvironmentSets;

/// <summary>
/// A target name may come from only one set on a project. Assigning a set, or changing one,
/// is refused when it would break that on any project it is (to be) assigned to.
/// </summary>
public static class EnvironmentSetConflicts
{
    /// <summary>
    /// Throws a <see cref="ConflictException"/> if <paramref name="entries"/>, the entries set
    /// <paramref name="setId"/> would have, repeat a name within the set or collide with another
    /// set on any of <paramref name="projectIds"/> (null: every project the set is assigned to).
    /// </summary>
    public static async Task EnsureNoConflicts(
        this CasterContext dbContext,
        Guid setId,
        IReadOnlyCollection<EntryEntity> entries,
        IReadOnlyCollection<Guid> projectIds,
        CancellationToken cancellationToken)
    {
        var duplicate = EnvironmentSetRules.GetDuplicateNames(entries).FirstOrDefault();

        if (duplicate is not null)
        {
            var entry = entries.First(x => x.EffectiveName == duplicate);
            throw new ConflictException($"This Environment Set already has the {EnvironmentSetRules.Describe(entry)}.");
        }

        if (entries.Count == 0)
        {
            return;
        }

        projectIds ??= await dbContext.ProjectEnvironmentSets
            .Where(x => x.EnvironmentSetId == setId)
            .Select(x => x.ProjectId)
            .ToListAsync(cancellationToken);

        if (projectIds.Count == 0)
        {
            return;
        }

        var others = await dbContext.ProjectEnvironmentSets
            .AsNoTracking()
            .Where(x => projectIds.Contains(x.ProjectId) && x.EnvironmentSetId != setId)
            .Select(x => new
            {
                ProjectName = x.Project.Name,
                SetName = x.EnvironmentSet.Name,
                Entries = x.EnvironmentSet.Entries.Select(e => new { e.Kind, e.TargetName }).ToList()
            })
            .ToListAsync(cancellationToken);

        var names = entries.ToDictionary(x => x.EffectiveName, StringComparer.Ordinal);

        foreach (var other in others.OrderBy(x => x.ProjectName).ThenBy(x => x.SetName))
        {
            foreach (var otherEntry in other.Entries)
            {
                var name = EntryEntity.GetEffectiveName(otherEntry.Kind, otherEntry.TargetName);

                if (names.TryGetValue(name, out var entry))
                {
                    throw new ConflictException(
                        $"The {EnvironmentSetRules.Describe(entry)} is already set by Environment Set \"{other.SetName}\" " +
                        $"on project \"{other.ProjectName}\". A name can come from only one set on a project.");
                }
            }
        }
    }
}
