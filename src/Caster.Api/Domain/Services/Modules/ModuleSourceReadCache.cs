// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Concurrent;

namespace Caster.Api.Domain.Services.Modules;

/// <summary>What a whole-source read found when it found no Module to store.</summary>
public enum EmptyReadOutcome
{
    /// <summary>No tags, and the default branch holds no Module root (or there are no commits).</summary>
    UntaggedNoModuleRoots,

    /// <summary>No tags, but the default branch holds Module roots.</summary>
    UntaggedWithModuleRoots,

    /// <summary>No tags, and HEAD names a branch that does not exist.</summary>
    UntaggedHeadUnreadable,

    /// <summary>Tags, but no Module root in any of them.</summary>
    TaggedNoModuleRoots,
}

/// <summary>
/// Remembers, per source, a whole-source read that found nothing to store,
/// keyed on what <c>ls-remote</c> returned. A source with Modules needs no such
/// cache - its stored versions are what the up-to-date check compares - but one
/// with none stores nothing, so without this every Module list would clone it
/// again.
/// </summary>
/// <remarks>
/// In memory like <see cref="ModuleSourceStatusStore"/>: each API instance
/// reads each such source once, then again only when its refs change, a sync
/// is forced, or the instance restarts.
/// </remarks>
public class ModuleSourceReadCache
{
    // Source names are case-insensitive everywhere: here, the sync lock, the
    // status store, the dispatcher, and the stored Module queries.
    private readonly ConcurrentDictionary<string, (string Fingerprint, EmptyReadOutcome Outcome)> _reads =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, (string RefsKey, bool HasBranches)> _branches =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The last read's outcome, if it found no Module and the refs are still <paramref name="fingerprint"/>.</summary>
    public bool TryGetEmpty(string sourceName, string fingerprint, out EmptyReadOutcome outcome)
    {
        if (_reads.TryGetValue(sourceName, out var entry) && entry.Fingerprint == fingerprint)
        {
            outcome = entry.Outcome;
            return true;
        }

        outcome = default;
        return false;
    }

    public void RecordEmpty(string sourceName, string fingerprint, EmptyReadOutcome outcome) =>
        _reads[sourceName] = (fingerprint, outcome);

    /// <summary>Drops the source's empty read, once a read stored Modules.</summary>
    public void Forget(string sourceName) => _reads.TryRemove(sourceName, out _);

    /// <summary>Whether any branch existed when the source's refs were last <paramref name="refsKey"/>.</summary>
    public bool TryGetHasBranches(string sourceName, string refsKey, out bool hasBranches)
    {
        if (_branches.TryGetValue(sourceName, out var entry) && entry.RefsKey == refsKey)
        {
            hasBranches = entry.HasBranches;
            return true;
        }

        hasBranches = false;
        return false;
    }

    public void RecordHasBranches(string sourceName, string refsKey, bool hasBranches) =>
        _branches[sourceName] = (refsKey, hasBranches);
}
