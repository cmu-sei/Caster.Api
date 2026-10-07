// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Infrastructure.Options;

namespace Caster.Api.Domain.Services.Modules;

/// <summary>
/// Puts the pre-existing Gitlab group scrape behind
/// <see cref="IModuleRepositoryProvider"/> without touching it. Everything it
/// needs still comes from the legacy <c>Terraform:Gitlab*</c> keys, so an
/// existing deployment keeps working with no config migration.
/// </summary>
/// <remarks>
/// <see cref="ModuleSourceOptions.Url"/> and
/// <see cref="ModuleSourceOptions.Layout"/> are ignored here: the Gitlab source
/// is identified by group id, and its module layout is fixed at one project per
/// Module.
/// </remarks>
public class GitlabModuleRepositoryProvider(IGitlabRepositoryService gitlab) : IModuleRepositoryProvider
{
    public string Provider => ModuleSourceProviders.Gitlab;

    public Task<bool> SyncModulesAsync(ModuleSourceOptions source, bool forceUpdate, CancellationToken cancellationToken) =>
        gitlab.GetModulesAsync(forceUpdate, cancellationToken);

    public Task<bool> SyncModuleAsync(ModuleSourceOptions source, string providerId, CancellationToken cancellationToken) =>
        gitlab.GetModuleAsync(providerId, cancellationToken);
}
