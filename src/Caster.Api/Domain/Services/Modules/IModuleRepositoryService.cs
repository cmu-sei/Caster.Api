// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Infrastructure.Options;

namespace Caster.Api.Domain.Services.Modules;

/// <summary>
/// The one thing the Modules feature talks to. Hides the fact that Modules can
/// come from more than one kind of repository.
/// </summary>
/// <remarks>
/// Implemented only by <see cref="ModuleRepositoryDispatcher"/>. Both methods
/// throw rather than return false on failure, because a silently empty Module
/// list is undiagnosable: see
/// <see cref="Infrastructure.Exceptions.ModuleSourceNotConfiguredException"/>
/// and <see cref="Infrastructure.Exceptions.ModuleSyncException"/>.
/// </remarks>
public interface IModuleRepositoryService
{
    /// <summary>
    /// Syncs every configured module source into the database.
    /// </summary>
    /// <param name="forceUpdate">Re-read every version even if nothing changed.</param>
    Task<bool> GetModulesAsync(bool forceUpdate, CancellationToken cancellationToken);

    /// <summary>
    /// Syncs a single Module. <paramref name="id"/> is either
    /// <c>&lt;sourceName&gt;</c> / <c>&lt;sourceName&gt;/&lt;subdirectory&gt;</c>
    /// for a git source, or a Gitlab project id for the legacy Gitlab source.
    /// </summary>
    Task<bool> GetModuleAsync(string id, CancellationToken cancellationToken);
}

/// <summary>
/// One concrete module repository reader. Implementations claim a
/// <see cref="Provider"/> name and are selected by
/// <see cref="ModuleRepositoryDispatcher"/>; they must not decide for
/// themselves whether they are the configured provider.
/// </summary>
public interface IModuleRepositoryProvider
{
    /// <summary>
    /// The <see cref="ModuleSourceOptions.Provider"/> value this reader
    /// handles, matched case insensitively. See <see cref="ModuleSourceProviders"/>.
    /// </summary>
    string Provider { get; }

    /// <summary>
    /// Discovers every Module in <paramref name="source"/> and upserts it,
    /// with its versions, variables and outputs.
    /// </summary>
    Task<bool> SyncModulesAsync(ModuleSourceOptions source, bool forceUpdate, CancellationToken cancellationToken);

    /// <summary>
    /// Discovers one Module in <paramref name="source"/>.
    /// <paramref name="providerId"/> is whatever remains of the caller's id
    /// after the source has been identified, and may be null to mean "the
    /// whole source".
    /// </summary>
    Task<bool> SyncModuleAsync(ModuleSourceOptions source, string providerId, CancellationToken cancellationToken);
}
