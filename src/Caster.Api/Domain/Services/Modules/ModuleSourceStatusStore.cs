// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Concurrent;

namespace Caster.Api.Domain.Services.Modules;

/// <summary>How the last sync of one module source went.</summary>
public class ModuleSourceStatus
{
    public DateTime? LastAttempt { get; init; }
    public DateTime? LastSuccess { get; init; }

    /// <summary>Why the last attempt failed, or null if it succeeded.</summary>
    public string Error { get; init; }
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

    public void RecordSuccess(string sourceName)
    {
        var now = DateTime.UtcNow;
        _statuses[sourceName] = new ModuleSourceStatus { LastAttempt = now, LastSuccess = now };
    }

    public void RecordFailure(string sourceName, string error)
    {
        _statuses.AddOrUpdate(
            sourceName,
            _ => new ModuleSourceStatus { LastAttempt = DateTime.UtcNow, Error = error },
            (_, previous) => new ModuleSourceStatus
            {
                LastAttempt = DateTime.UtcNow,
                LastSuccess = previous.LastSuccess,
                Error = error,
            });
    }

    /// <summary>The source's last sync, or null if this instance has not synced it yet.</summary>
    public ModuleSourceStatus Get(string sourceName) =>
        _statuses.TryGetValue(sourceName, out var status) ? status : null;
}
