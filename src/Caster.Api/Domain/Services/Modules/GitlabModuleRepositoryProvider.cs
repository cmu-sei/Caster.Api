// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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
public class GitlabModuleRepositoryProvider(
    IGitlabRepositoryService gitlab,
    ILogger<GitlabModuleRepositoryProvider> logger) : IModuleRepositoryProvider
{
    public string Provider => ModuleSourceProviders.Gitlab;

    public async Task<ModuleSyncResult> SyncModulesAsync(ModuleSourceOptions source, bool forceUpdate, CancellationToken cancellationToken)
    {
        RefuseCredentials(source);
        await gitlab.GetModulesAsync(forceUpdate, cancellationToken);
        return ModuleSyncResult.Ok;
    }

    public Task<bool> SyncModuleAsync(ModuleSourceOptions source, string providerId, CancellationToken cancellationToken)
    {
        RefuseCredentials(source);
        return gitlab.GetModuleAsync(providerId, cancellationToken);
    }

    /// <summary>
    /// The api url is only ever an http client's base address, and the token
    /// has its own setting, so userinfo in it is a credential in the wrong
    /// place: refused like a git source's.
    /// </summary>
    private void RefuseCredentials(ModuleSourceOptions source)
    {
        if (source.Url?.Contains('@') == true)
        {
            logger.LogWarning("Refusing module source {Source}: its url contains credentials.", source.Name);
            throw new ModuleSourceRefusedException(ModuleSourceUrl.CredentialsMessage);
        }
    }
}
