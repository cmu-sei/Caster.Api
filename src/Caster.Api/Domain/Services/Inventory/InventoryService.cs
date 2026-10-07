// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Infrastructure.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Caster.Api.Domain.Services.Inventory;

public interface IInventoryService
{
    /// <summary>
    /// Returns the cached inventory for a category. Never throws and never blocks
    /// on the provider.
    /// </summary>
    InventorySnapshot GetSnapshot();

    /// <summary>
    /// Wakes the background refresh loop so the cache is rebuilt immediately.
    /// Returns without waiting for the result.
    /// </summary>
    void ForceRefresh();

    /// <summary>
    /// Wakes the background refresh loop and waits for that refresh to land in
    /// the cache, giving up the wait - not the work - after <paramref name="timeout"/>.
    /// Returns true if the refresh completed, so a caller that immediately reads
    /// the cache is guaranteed to see the new snapshot. Returns false if the wait
    /// expired or the caller disconnected; the refresh is still running in the
    /// background in that case.
    /// </summary>
    Task<bool> ForceRefreshAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>
/// Polls the configured infrastructure provider on an interval and serves the
/// results from memory. Follows the same shape as <see cref="Terraform.ImageTagService"/>.
/// </summary>
public class InventoryService : BackgroundService, IInventoryService
{
    private readonly IInventoryProviderClient _client;
    private readonly ILogger<InventoryService> _logger;

    private InfrastructureOptions _options;
    private InventorySnapshot _snapshot = InventorySnapshot.AllUnavailable(InventoryMessages.NotEnabled);
    private TaskCompletionSource<bool> _taskCompletionSource = new TaskCompletionSource<bool>();

    // Completed every time a refresh lands in the cache, then replaced. Lets a
    // caller wait for the *next* refresh without ever holding the provider call.
    private readonly object _refreshLock = new();
    private TaskCompletionSource<bool> _refreshCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public InventoryService(
        IOptionsMonitor<InfrastructureOptions> optionsMonitor,
        IInventoryProviderClient client,
        ILogger<InventoryService> logger)
    {
        _client = client;
        _options = optionsMonitor.CurrentValue;
        _logger = logger;

        optionsMonitor.OnChange(x =>
        {
            _options = optionsMonitor.CurrentValue;
            this.ForceRefresh();
        });
    }

    public InventorySnapshot GetSnapshot()
    {
        return _snapshot;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await DoWork(_options, cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Wait(_options, stoppingToken);

            // don't query the provider on the way out
            if (stoppingToken.IsCancellationRequested)
                break;

            await DoWork(_options, stoppingToken);
        }
    }

    public void ForceRefresh()
    {
        _taskCompletionSource.TrySetCanceled();
    }

    public async Task<bool> ForceRefreshAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        // Subscribe before triggering, so a fast refresh cannot complete between
        // the two and leave us waiting for the one after it.
        Task completion;

        lock (_refreshLock)
        {
            completion = _refreshCompletion.Task;
        }

        ForceRefresh();

        // The provider call runs on the background service with the host's
        // stopping token. The request's token only bounds how long we wait here,
        // so a disconnected http caller never cancels the refresh itself.
        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        waitCts.CancelAfter(timeout);

        var finished = await Task.WhenAny(completion, Task.Delay(Timeout.Infinite, waitCts.Token));

        return finished == completion;
    }

    private async Task DoWork(InfrastructureOptions options, CancellationToken cancellationToken)
    {
        try
        {
            if (!options.Enabled)
            {
                _logger.LogInformation("Infrastructure inventory disabled, skipping inventory query");
                _snapshot = InventorySnapshot.AllUnavailable(InventoryMessages.NotEnabled);
                return;
            }

            _logger.LogInformation("Querying {Provider} inventory at {ProviderUrl}...", options.Provider, options.Url);
            _snapshot = await _client.GetInventoryAsync(options, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception getting infrastructure inventory.");

            var message = InventoryRedaction.Redact(
                $"Could not read the infrastructure inventory: {ex.Message}", options);

            _snapshot = InventorySnapshot.AllUnavailable(message, _snapshot?.LastUpdated);
        }
        finally
        {
            // _snapshot is assigned before this fires, so anyone released by the
            // signal is guaranteed to observe the new value.
            SignalRefreshComplete();
        }
    }

    private void SignalRefreshComplete()
    {
        TaskCompletionSource<bool> current;

        lock (_refreshLock)
        {
            current = _refreshCompletion;
            _refreshCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        current.TrySetResult(true);
    }

    private async Task Wait(InfrastructureOptions options, CancellationToken cancellationToken)
    {
        var minutes = options.RefreshMinutes > 0 ? options.RefreshMinutes : 30;

        await Task.WhenAny(Task.Delay(TimeSpan.FromMinutes(minutes), cancellationToken), _taskCompletionSource.Task);

        if (_taskCompletionSource.Task.IsCanceled)
        {
            _taskCompletionSource = new TaskCompletionSource<bool>();
        }
    }
}
