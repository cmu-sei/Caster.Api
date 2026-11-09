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

    /// <summary>
    /// Throws a <see cref="ConflictException"/> if the proposed final entries of several sets at
    /// once (<paramref name="finalEntries"/>, by set id) repeat a name within a set, or give one
    /// name twice on any project those sets are assigned to, counting every other set on those
    /// projects as it is stored. Checking each set alone against stored data would miss two
    /// edited sets on one project gaining the same name.
    /// </summary>
    public static async Task EnsureNoConflictsInFinalState(
        this CasterContext dbContext,
        IReadOnlyDictionary<Guid, (string Name, List<EntryEntity> Entries)> finalEntries,
        CancellationToken cancellationToken)
    {
        foreach (var (_, (setName, entries)) in finalEntries)
        {
            var duplicate = EnvironmentSetRules.GetDuplicateNames(entries).FirstOrDefault();

            if (duplicate is not null)
            {
                var entry = entries.First(x => x.EffectiveName == duplicate);
                throw new ConflictException($"Environment Set \"{setName}\" would have the {EnvironmentSetRules.Describe(entry)} twice.");
            }
        }

        var setIds = finalEntries.Keys.ToList();

        var projectIds = await dbContext.ProjectEnvironmentSets
            .Where(x => setIds.Contains(x.EnvironmentSetId))
            .Select(x => x.ProjectId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (projectIds.Count == 0)
        {
            return;
        }

        var assignments = await dbContext.ProjectEnvironmentSets
            .AsNoTracking()
            .Where(x => projectIds.Contains(x.ProjectId))
            .Select(x => new
            {
                x.ProjectId,
                ProjectName = x.Project.Name,
                x.EnvironmentSetId,
                SetName = x.EnvironmentSet.Name,
                Entries = x.EnvironmentSet.Entries.Select(e => new { e.Kind, e.TargetName }).ToList()
            })
            .ToListAsync(cancellationToken);

        foreach (var project in assignments.GroupBy(x => (x.ProjectId, x.ProjectName)).OrderBy(x => x.Key.ProjectName))
        {
            var givenBy = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var assignment in project.OrderBy(x => x.SetName))
            {
                var names = finalEntries.TryGetValue(assignment.EnvironmentSetId, out var proposed)
                    ? proposed.Entries.Select(x => (x.EffectiveName, Described: EnvironmentSetRules.Describe(x)))
                    : assignment.Entries.Select(x =>
                    {
                        var entry = new EntryEntity { Kind = x.Kind, TargetName = x.TargetName };
                        return (entry.EffectiveName, Described: EnvironmentSetRules.Describe(entry));
                    });

                foreach (var (name, described) in names)
                {
                    if (givenBy.TryGetValue(name, out var otherSet))
                    {
                        throw new ConflictException(
                            $"The {described} would be set by both Environment Set \"{otherSet}\" and \"{assignment.SetName}\" " +
                            $"on project \"{project.Key.ProjectName}\". A name can come from only one set on a project.");
                    }

                    givenBy[name] = assignment.SetName;
                }
            }
        }
    }
}
