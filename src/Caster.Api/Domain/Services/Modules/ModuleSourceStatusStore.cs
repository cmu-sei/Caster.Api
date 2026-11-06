// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Concurrent;

namespace Caster.Api.Domain.Services.Modules;

/// <summary>How the last sync of one module source went.</summary>
public class ModuleSourceStatus
{
    public DateTime? LastAttempt { get; init; }

    /// <summary>When the source was last read without error, including a read that found it Empty or a Warning.</summary>
    public DateTime? LastSuccess { get; init; }

    public ModuleSourceState State { get; init; }

    /// <summary>What to tell an administrator: set for Empty, Warning and Error (why it failed), null for Ok.</summary>
    public string Message { get; init; }
}

/// <summary>
/// Remembers each module source's last sync, so a failure can be reported
/// where an administrator looks for it instead of failing every Module list.
/// </summary>
/// <remarks>
/// In memory, so each API instance reports its own syncs, and a restart forgets
/// them until the next Module list request syncs again.
/// </remarks>
public class ModuleSourceStatusStore
{
    private readonly ConcurrentDictionary<string, ModuleSourceStatus> _statuses =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Records a read that did not fail, whether it found Modules (Ok) or not (Empty, Warning).</summary>
    public void RecordSuccess(string sourceName, ModuleSyncResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        // A sync that read nothing has nothing to record; see ModuleSyncResult.NotRead.
        if (!result.Read || result.State == null)
        {
            throw new ArgumentException("Only a sync that read the source can be recorded.", nameof(result));
        }

        var now = DateTime.UtcNow;

        _statuses[sourceName] = new ModuleSourceStatus
        {
            LastAttempt = now,
            LastSuccess = now,
            State = result.State.Value,
            Message = result.Message,
        };
    }

    public void RecordFailure(string sourceName, string error)
    {
        _statuses.AddOrUpdate(
            sourceName,
            _ => new ModuleSourceStatus
            {
                LastAttempt = DateTime.UtcNow,
                State = ModuleSourceState.Error,
                Message = error,
            },
            (_, previous) => new ModuleSourceStatus
            {
                LastAttempt = DateTime.UtcNow,
                LastSuccess = previous.LastSuccess,
                State = ModuleSourceState.Error,
                Message = error,
            });
    }

    /// <summary>The source's last sync, or null if this instance has not synced it yet.</summary>
    public ModuleSourceStatus Get(string sourceName) =>
        _statuses.TryGetValue(sourceName, out var status) ? status : null;
}
