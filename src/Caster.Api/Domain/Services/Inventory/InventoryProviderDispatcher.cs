// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Infrastructure.Options;
using Microsoft.Extensions.Logging;

namespace Caster.Api.Domain.Services.Inventory;

/// <summary>
/// One concrete infrastructure provider reader. Implementations claim a
/// <see cref="Provider"/> name and are selected by
/// <see cref="InventoryProviderDispatcher"/>; they must not inspect
/// <see cref="InfrastructureOptions.Provider"/> themselves.
/// </summary>
public interface IInventoryProvider : IInventoryProviderClient
{
    /// <summary>
    /// The <see cref="InfrastructureOptions.Provider"/> value this reader handles,
    /// matched case insensitively. See <see cref="InfrastructureProviders"/>.
    /// </summary>
    string Provider { get; }

    /// <summary>
    /// Name of this provider's <see cref="System.Net.Http.IHttpClientFactory"/>
    /// client, so each provider gets its own timeout and TLS handler.
    /// </summary>
    string HttpClientName { get; }
}

/// <summary>
/// Selects the <see cref="IInventoryProvider"/> that matches
/// <see cref="InfrastructureOptions.Provider"/>. Registered as the single
/// <see cref="IInventoryProviderClient"/> so <see cref="InventoryService"/> stays
/// unaware that there is more than one provider.
/// </summary>
public class InventoryProviderDispatcher : IInventoryProviderClient
{
    private readonly Dictionary<string, IInventoryProvider> _providers;
    private readonly ILogger<InventoryProviderDispatcher> _logger;

    public InventoryProviderDispatcher(
        IEnumerable<IInventoryProvider> providers,
        ILogger<InventoryProviderDispatcher> logger)
    {
        _providers = (providers ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.Provider))
            .GroupBy(x => x.Provider, StringComparer.OrdinalIgnoreCase)
            // Last registration wins, so a deployment can substitute a provider.
            .ToDictionary(x => x.Key, x => x.Last(), StringComparer.OrdinalIgnoreCase);

        _logger = logger;
    }

    public Task<InventorySnapshot> GetInventoryAsync(InfrastructureOptions options, CancellationToken cancellationToken)
    {
        var requested = options?.Provider;

        if (!string.IsNullOrWhiteSpace(requested) &&
            _providers.TryGetValue(requested.Trim(), out var provider))
        {
            return provider.GetInventoryAsync(options, cancellationToken);
        }

        _logger.LogWarning(
            "No infrastructure inventory provider is registered for '{Provider}'. Registered providers: {RegisteredProviders}",
            requested,
            string.Join(", ", _providers.Keys));

        return Task.FromResult(InventorySnapshot.AllUnavailable(InventoryMessages.UnsupportedProvider(requested)));
    }
}
