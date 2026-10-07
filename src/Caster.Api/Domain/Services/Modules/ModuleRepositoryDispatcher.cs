// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Caster.Api.Domain.Services.Modules;

/// <summary>
/// Resolves the configured module sources and fans a sync out across them.
/// Registered as the only <see cref="IModuleRepositoryService"/>, so the
/// Modules feature stays unaware that there is more than one kind of source.
/// </summary>
public class ModuleRepositoryDispatcher : IModuleRepositoryService
{
    private readonly Dictionary<string, IModuleRepositoryProvider> _providers;
    private readonly IOptionsMonitor<TerraformOptions> _terraformOptions;
    private readonly ILogger<ModuleRepositoryDispatcher> _logger;

    public ModuleRepositoryDispatcher(
        IEnumerable<IModuleRepositoryProvider> providers,
        IOptionsMonitor<TerraformOptions> terraformOptions,
        ILogger<ModuleRepositoryDispatcher> logger)
    {
        _providers = (providers ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.Provider))
            .GroupBy(x => x.Provider, StringComparer.OrdinalIgnoreCase)
            // Last registration wins, so a deployment can substitute a provider.
            .ToDictionary(x => x.Key, x => x.Last(), StringComparer.OrdinalIgnoreCase);

        _terraformOptions = terraformOptions;
        _logger = logger;
    }

    public async Task<bool> GetModulesAsync(bool forceUpdate, CancellationToken cancellationToken)
    {
        var sources = GetSources();

        if (sources.Count == 0)
        {
            throw new ModuleSourceNotConfiguredException();
        }

        var failures = new List<string>();

        foreach (var source in sources)
        {
            if (!TryGetProvider(source, out var provider, out var failure))
            {
                failures.Add(failure);
                continue;
            }

            try
            {
                await provider.SyncModulesAsync(source, forceUpdate, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Sources are independent, so one broken source must not hide
                // the others. Whatever synced is already persisted; the caller
                // is told what did not.
                _logger.LogError(ex, "Failed to sync Modules from module source {Source}.", source.Name);
                failures.Add($"module source '{source.Name}': {ex.Message}");
            }
        }

        if (failures.Count > 0)
        {
            throw new ModuleSyncException(failures);
        }

        return true;
    }

    public async Task<bool> GetModuleAsync(string id, CancellationToken cancellationToken)
    {
        var sources = GetSources();

        if (sources.Count == 0)
        {
            throw new ModuleSourceNotConfiguredException();
        }

        var (source, providerId) = ResolveSource(id, sources);

        if (!TryGetProvider(source, out var provider, out var failure))
        {
            throw new ModuleSyncException(failure);
        }

        return await provider.SyncModuleAsync(source, providerId, cancellationToken);
    }

    /// <summary>
    /// The legacy Gitlab group, when configured, plus every configured
    /// <c>Terraform:ModuleSources</c> entry. Both kinds can be present at once.
    /// </summary>
    private List<ModuleSourceOptions> GetSources()
    {
        var options = _terraformOptions.CurrentValue;
        var sources = new List<ModuleSourceOptions>();

        if (options == null)
        {
            return sources;
        }

        // Both keys are required: the group id names what to list, and the api
        // url is what the "gitlab" http client is pointed at, which throws on
        // an empty string.
        if (options.GitlabGroupId.HasValue && !string.IsNullOrWhiteSpace(options.GitlabApiUrl))
        {
            sources.Add(new ModuleSourceOptions
            {
                Name = GitlabSourceName,
                Description = "Gitlab module group",
                Provider = ModuleSourceProviders.Gitlab,
                Url = options.GitlabApiUrl,
            });
        }

        foreach (var source in options.ModuleSources ?? [])
        {
            if (source == null)
            {
                continue;
            }

            if (!source.IsConfigured)
            {
                _logger.LogWarning(
                    "Ignoring a Terraform:ModuleSources entry with no Name or no Url (Name '{Name}').",
                    source.Name);

                continue;
            }

            sources.Add(source);
        }

        return sources;
    }

    /// <summary>
    /// Name the synthesized legacy source carries. Gitlab Modules key off the
    /// project's path_with_namespace rather than this, so it never appears in a
    /// Module Path and changing it migrates nothing.
    /// </summary>
    public const string GitlabSourceName = "gitlab";

    /// <summary>
    /// A git source is addressed as <c>&lt;sourceName&gt;</c> or
    /// <c>&lt;sourceName&gt;/&lt;subdirectory&gt;</c>. Anything that matches no
    /// source name falls through to the legacy Gitlab source, where it is a
    /// project id - which is exactly what this endpoint accepted before.
    /// </summary>
    private (ModuleSourceOptions Source, string ProviderId) ResolveSource(
        string id,
        List<ModuleSourceOptions> sources)
    {
        var trimmed = (id ?? string.Empty).Trim().Trim('/');

        if (trimmed.Length > 0)
        {
            var firstSegment = trimmed.Split('/', 2)[0];

            var named = sources.FirstOrDefault(x =>
                string.Equals(x.Name, firstSegment, StringComparison.OrdinalIgnoreCase));

            if (named != null)
            {
                var remainder = trimmed.Length > firstSegment.Length
                    ? trimmed[(firstSegment.Length + 1)..]
                    : null;

                return (named, remainder);
            }
        }

        var gitlab = sources.FirstOrDefault(x =>
            string.Equals(x.Provider, ModuleSourceProviders.Gitlab, StringComparison.OrdinalIgnoreCase));

        if (gitlab != null)
        {
            return (gitlab, trimmed);
        }

        throw new EntityNotFoundException<Models.Module>(
            $"'{id}' does not name any configured module source. Use '<sourceName>' or " +
            $"'<sourceName>/<subdirectory>'. Configured sources: {string.Join(", ", sources.Select(x => x.Name))}.");
    }

    private bool TryGetProvider(
        ModuleSourceOptions source,
        out IModuleRepositoryProvider provider,
        out string failure)
    {
        var requested = source.Provider;

        if (!string.IsNullOrWhiteSpace(requested) &&
            _providers.TryGetValue(requested.Trim(), out provider))
        {
            failure = null;
            return true;
        }

        provider = null;
        failure = $"module source '{source.Name}' names provider '{requested}', " +
                  $"which is not registered. Registered providers: {string.Join(", ", _providers.Keys)}.";

        _logger.LogError("{Failure}", failure);

        return false;
    }
}
