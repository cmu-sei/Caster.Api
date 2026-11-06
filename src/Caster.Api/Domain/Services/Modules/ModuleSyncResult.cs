// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Caster.Api.Domain.Services.Modules;

/// <summary>What the last sync of a module source found.</summary>
public enum ModuleSourceState
{
    /// <summary>Synced, and offers at least one Module.</summary>
    Ok,

    /// <summary>Read fine, but holds no Module yet. Not a failure: a newly seeded repository looks like this.</summary>
    Empty,

    /// <summary>Read fine, but something keeps its Modules from being used, such as having no version tags.</summary>
    Warning,

    /// <summary>Could not be read: listing tags, cloning, authentication or a module file failed.</summary>
    Error,
}

/// <summary>
/// The outcome of a whole-source sync that did not fail. A failure is still an
/// exception, so a caller cannot mistake one for an empty source. State is
/// null only for <see cref="NotRead"/>.
/// </summary>
public sealed record ModuleSyncResult(ModuleSourceState? State, string Message)
{
    /// <summary>
    /// False when the source was not read at all, as when another request held
    /// its sync lock too long. Such a result says nothing about the source, so
    /// it must not replace the last recorded status.
    /// </summary>
    public bool Read { get; init; } = true;

    public static readonly ModuleSyncResult Ok = new(ModuleSourceState.Ok, null);

    /// <summary>The source was not read; keep whatever status it had.</summary>
    public static readonly ModuleSyncResult NotRead = new(null, null) { Read = false };

    public static ModuleSyncResult Empty(string message) => new(ModuleSourceState.Empty, message);

    public static ModuleSyncResult Warning(string message) => new(ModuleSourceState.Warning, message);
}
