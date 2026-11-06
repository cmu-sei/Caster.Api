// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

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

/// <summary>An empty read, and the Module Paths found on the default branch (untagged Modules), if any.</summary>
public sealed record EmptyRead(EmptyReadOutcome Outcome, IReadOnlyCollection<string> DefaultBranchPaths)
{
    public static EmptyRead Of(EmptyReadOutcome outcome) => new(outcome, []);
}

/// <summary>
/// Remembers, per source, the last real whole-source read, keyed on what
/// <c>ls-remote</c> returned. For a read that found nothing to store, it holds
/// what was found, so the next Module list need not clone again: a source with
/// no Module stores nothing for the up-to-date check to compare. For a read
/// that stored Modules, it records only that this exact set of refs was read,
/// which tells the up-to-date check whether the Modules it marked missing are
/// still current.
/// </summary>
/// <remarks>
/// In memory like <see cref="ModuleSourceStatusStore"/>: each API instance
/// reads each source again after a restart.
/// </remarks>
public class ModuleSourceReadCache
{
    // Source names are case-insensitive everywhere: here, the sync lock, the
    // status store, the dispatcher, and the SourceName queries.
    private readonly ConcurrentDictionary<string, (string Fingerprint, EmptyRead Read)> _reads =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The last read's result, if it found no Module and the refs are still <paramref name="fingerprint"/>.</summary>
    public bool TryGetEmpty(string sourceName, string fingerprint, out EmptyRead read)
    {
        if (_reads.TryGetValue(sourceName, out var entry) && entry.Fingerprint == fingerprint && entry.Read != null)
        {
            read = entry.Read;
            return true;
        }

        read = null;
        return false;
    }

    /// <summary>Whether the last real read of the source was of exactly these refs.</summary>
    public bool LastReadWas(string sourceName, string fingerprint) =>
        _reads.TryGetValue(sourceName, out var entry) && entry.Fingerprint == fingerprint;

    public void RecordEmpty(string sourceName, string fingerprint, EmptyRead read)
    {
        ArgumentNullException.ThrowIfNull(read);
        _reads[sourceName] = (fingerprint, read);
    }

    public void RecordStored(string sourceName, string fingerprint) =>
        _reads[sourceName] = (fingerprint, null);

    private readonly ConcurrentDictionary<string, bool> _synced = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, (string RefsKey, bool HasBranches)> _branches =
        new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>Whether this API instance has yet to give the source's older Modules their SourceName.</summary>
    public bool NeedsBackfill(string sourceName) => !_synced.ContainsKey(sourceName);

    public void MarkBackfilled(string sourceName) => _synced[sourceName] = true;
}
