// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Services.EnvironmentSets;
using Caster.Api.Domain.Services.ProjectGroups;
using Caster.Api.Infrastructure.Exceptions;
using Microsoft.EntityFrameworkCore;
using EntryEntity = Caster.Api.Domain.Models.EnvironmentSetEntry;

namespace Caster.Api.Features.EnvironmentSets;

/// <summary>
/// A change that could make two sets on one project give the same name: entry edits, new
/// assignments to projects or groups, and projects moving between groups. Checked together,
/// against the whole final state.
/// </summary>
public class ProposedSetChanges
{
    /// <summary>The final entries of sets being edited, by set id.</summary>
    public Dictionary<Guid, (string Name, List<EntryEntity> Entries)> Entries { get; } = [];

    /// <summary>Sets being assigned directly to projects.</summary>
    public List<(Guid ProjectId, Guid SetId)> ProjectAssignments { get; } = [];

    /// <summary>Sets being assigned to project groups.</summary>
    public List<(Guid ProjectGroupId, Guid SetId)> GroupAssignments { get; } = [];

    /// <summary>Projects moving into a group (or out of one, to null).</summary>
    public Dictionary<Guid, Guid?> ProjectGroupChanges { get; } = [];

    /// <summary>Sets being taken off projects, so a set can be swapped for one giving the same names.</summary>
    public List<(Guid ProjectId, Guid SetId)> ProjectUnassignments { get; } = [];

    /// <summary>Sets being taken off project groups.</summary>
    public List<(Guid ProjectGroupId, Guid SetId)> GroupUnassignments { get; } = [];
}

/// <summary>
/// A target name may come from only one set on a project, counting the sets it gets through its
/// group. A change is refused when it would break that on any project it reaches.
/// </summary>
public static class EnvironmentSetConflicts
{
    /// <summary>
    /// Throws a <see cref="ConflictException"/> if <paramref name="entries"/>, the entries set
    /// <paramref name="setId"/> would have, repeat a name within the set or collide with another
    /// set on any of <paramref name="projectIds"/> (null: every project the set reaches), as if
    /// it were assigned to each of them.
    /// </summary>
    public static Task EnsureNoConflicts(
        this CasterContext dbContext,
        Guid setId,
        IReadOnlyCollection<EntryEntity> entries,
        IReadOnlyCollection<Guid> projectIds,
        CancellationToken cancellationToken)
    {
        var changes = new ProposedSetChanges();
        changes.Entries[setId] = (null, entries.ToList());

        foreach (var projectId in projectIds ?? [])
        {
            changes.ProjectAssignments.Add((projectId, setId));
        }

        return dbContext.EnsureNoConflicts(changes, cancellationToken);
    }

    /// <summary>
    /// The final entries of several sets at once (<paramref name="finalEntries"/>, by set id).
    /// Checking each set alone against stored data would miss two edited sets on one project
    /// gaining the same name.
    /// </summary>
    public static Task EnsureNoConflictsInFinalState(
        this CasterContext dbContext,
        IReadOnlyDictionary<Guid, (string Name, List<EntryEntity> Entries)> finalEntries,
        CancellationToken cancellationToken)
    {
        var changes = new ProposedSetChanges();

        foreach (var (id, value) in finalEntries)
        {
            changes.Entries[id] = value;
        }

        return dbContext.EnsureNoConflicts(changes, cancellationToken);
    }

    public static async Task EnsureNoConflicts(this CasterContext dbContext, ProposedSetChanges changes, CancellationToken cancellationToken)
    {
        foreach (var (_, (setName, entries)) in changes.Entries)
        {
            var duplicate = EnvironmentSetRules.GetDuplicateNames(entries).FirstOrDefault();

            if (duplicate is not null)
            {
                var entry = entries.First(x => x.EffectiveName == duplicate);
                throw new ConflictException(setName is null
                    ? $"This Environment Set already has the {EnvironmentSetRules.Describe(entry)}."
                    : $"Environment Set \"{setName}\" would have the {EnvironmentSetRules.Describe(entry)} twice.");
            }
        }

        // Every project the change reaches, now or after it.
        var setIds = changes.Entries.Keys
            .Concat(changes.ProjectAssignments.Select(x => x.SetId))
            .Concat(changes.GroupAssignments.Select(x => x.SetId))
            .Distinct()
            .ToList();
        var addedGroupIds = changes.GroupAssignments.Select(x => x.ProjectGroupId).Distinct().ToList();

        var affected = (await dbContext.EffectiveSetAssignments()
                .Where(x => setIds.Contains(x.EnvironmentSetId))
                .Select(x => x.ProjectId)
                .ToListAsync(cancellationToken))
            .Concat(await dbContext.Projects
                .Where(x => x.ProjectGroupId.HasValue && addedGroupIds.Contains(x.ProjectGroupId.Value))
                .Select(x => x.Id)
                .ToListAsync(cancellationToken))
            .Concat(changes.ProjectAssignments.Select(x => x.ProjectId))
            .Concat(changes.ProjectGroupChanges.Keys)
            .Distinct()
            .ToList();

        // A group's own sets must agree even before it has projects, so that a project put in
        // it later, or created in it, gets a consistent set of names.
        foreach (var groupId in addedGroupIds)
        {
            var groupSets = await dbContext.ProjectGroupEnvironmentSets
                .Where(x => x.ProjectGroupId == groupId)
                .Select(x => x.EnvironmentSetId)
                .ToListAsync(cancellationToken);
            var finalGroupSets = groupSets
                .Where(x => !changes.GroupUnassignments.Contains((groupId, x)))
                .Concat(changes.GroupAssignments.Where(x => x.ProjectGroupId == groupId).Select(x => x.SetId))
                .Distinct()
                .ToList();
            var groupName = await dbContext.ProjectGroups.Where(x => x.Id == groupId).Select(x => x.Name).FirstOrDefaultAsync(cancellationToken);
            var sets = await dbContext.EnvironmentSets
                .AsNoTracking()
                .Where(x => finalGroupSets.Contains(x.Id))
                .Select(x => new { x.Id, x.Name, Entries = x.Entries.Select(e => new { e.Kind, e.TargetName }).ToList() })
                .ToListAsync(cancellationToken);
            var givenBy = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var set in sets.OrderBy(x => x.Name))
            {
                var entries = changes.Entries.TryGetValue(set.Id, out var proposed)
                    ? proposed.Entries
                    : set.Entries.Select(e => new EntryEntity { Kind = e.Kind, TargetName = e.TargetName }).ToList();

                foreach (var entry in entries)
                {
                    if (givenBy.TryGetValue(entry.EffectiveName, out var otherSet))
                    {
                        throw new ConflictException(
                            $"The {EnvironmentSetRules.Describe(entry)} would be set by both Environment Set \"{otherSet}\" and \"{set.Name}\" " +
                            $"in project group \"{groupName}\". A name can come from only one set on a project.");
                    }

                    givenBy[entry.EffectiveName] = set.Name;
                }
            }
        }

        if (affected.Count == 0)
        {
            return;
        }

        var projects = await dbContext.Projects
            .AsNoTracking()
            .Where(x => affected.Contains(x.Id))
            .Select(x => new { x.Id, x.Name, x.ProjectGroupId })
            .ToListAsync(cancellationToken);

        var finalGroup = projects.ToDictionary(
            x => x.Id,
            x => changes.ProjectGroupChanges.TryGetValue(x.Id, out var moved) ? moved : x.ProjectGroupId);
        var groupIds = finalGroup.Values.Where(x => x.HasValue).Select(x => x.Value).Distinct().ToList();

        var direct = await dbContext.ProjectEnvironmentSets
            .AsNoTracking()
            .Where(x => affected.Contains(x.ProjectId))
            .Select(x => new { x.ProjectId, x.EnvironmentSetId })
            .ToListAsync(cancellationToken);
        var viaGroup = await dbContext.ProjectGroupEnvironmentSets
            .AsNoTracking()
            .Where(x => groupIds.Contains(x.ProjectGroupId))
            .Select(x => new { x.ProjectGroupId, x.EnvironmentSetId })
            .ToListAsync(cancellationToken);

        var finalSets = projects.ToDictionary(
            x => x.Id,
            x => direct.Where(d => d.ProjectId == x.Id).Select(d => d.EnvironmentSetId)
                .Where(d => !changes.ProjectUnassignments.Contains((x.Id, d)))
                .Concat(changes.ProjectAssignments.Where(a => a.ProjectId == x.Id).Select(a => a.SetId))
                .Concat(finalGroup[x.Id] is { } group
                    ? viaGroup.Where(g => g.ProjectGroupId == group).Select(g => g.EnvironmentSetId)
                        .Where(g => !changes.GroupUnassignments.Contains((group, g)))
                        .Concat(changes.GroupAssignments.Where(a => a.ProjectGroupId == group).Select(a => a.SetId))
                    : [])
                .Distinct()
                .ToList());

        var allSetIds = finalSets.Values.SelectMany(x => x).Distinct().ToList();
        var stored = await dbContext.EnvironmentSets
            .AsNoTracking()
            .Where(x => allSetIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.Name,
                Entries = x.Entries.Select(e => new { e.Kind, e.TargetName }).ToList()
            })
            .ToListAsync(cancellationToken);
        var setNames = stored.ToDictionary(x => x.Id, x => x.Name);

        IEnumerable<EntryEntity> EntriesOf(Guid setId) =>
            changes.Entries.TryGetValue(setId, out var proposed)
                ? proposed.Entries
                : stored.Where(x => x.Id == setId)
                    .SelectMany(x => x.Entries)
                    .Select(e => new EntryEntity { Kind = e.Kind, TargetName = e.TargetName });

        foreach (var project in projects.OrderBy(x => x.Name))
        {
            var givenBy = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var setId in finalSets[project.Id].OrderBy(x => setNames.GetValueOrDefault(x)))
            {
                var setName = setNames.GetValueOrDefault(setId) ?? changes.Entries.GetValueOrDefault(setId).Name;

                foreach (var entry in EntriesOf(setId))
                {
                    if (givenBy.TryGetValue(entry.EffectiveName, out var otherSet))
                    {
                        throw new ConflictException(
                            $"The {EnvironmentSetRules.Describe(entry)} would be set by both Environment Set \"{otherSet}\" and \"{setName}\" " +
                            $"on project \"{project.Name}\". A name can come from only one set on a project.");
                    }

                    givenBy[entry.EffectiveName] = setName;
                }
            }
        }
    }
}
