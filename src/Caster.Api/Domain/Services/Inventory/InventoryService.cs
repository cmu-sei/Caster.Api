// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Infrastructure.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Caster.Api.Domain.Services.Inventory;

/// <summary>
/// A configured inventory connection, as listed to clients. Never carries the
/// url or credentials.
/// </summary>
public class InventoryConnectionInfo
{
    public string Name { get; set; }

    public string Provider { get; set; }

    public bool Enabled { get; set; }

    /// <summary>
    /// True for the connection configured by the top-level Infrastructure settings.
    /// </summary>
    public bool IsDefault { get; set; }

    public DateTime? LastUpdated { get; set; }
}

public interface IInventoryService
{
    /// <summary>
    /// Every configured connection, the default first and the rest by name.
    /// </summary>
    IReadOnlyList<InventoryConnectionInfo> GetConnections();

    /// <summary>
    /// True if <paramref name="connection"/> is configured. Null or empty names
    /// the default connection, which always exists. Names are case insensitive.
    /// </summary>
    bool HasConnection(string connection);

    /// <summary>
    /// Returns the cached inventory for a connection, or for the default one when
    /// <paramref name="connection"/> is null or empty. Never blocks on the
    /// provider. Returns null for a connection that is not configured.
    /// </summary>
    InventorySnapshot GetSnapshot(string connection = null);

    /// <summary>
    /// Wakes a connection's background refresh loop so its cache is rebuilt
    /// immediately. Returns without waiting for the result.
    /// </summary>
    void ForceRefresh(string connection = null);

    /// <summary>
    /// <see cref="ForceRefreshAsync(string, TimeSpan, CancellationToken)"/> for
    /// the default connection.
    /// </summary>
    Task<bool> ForceRefreshAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>
    /// Wakes a connection's background refresh loop and waits for that refresh to
    /// land in its cache, giving up the wait - not the work - after
    /// <paramref name="timeout"/>. Returns true if the refresh completed, so a
    /// caller that immediately reads the cache is guaranteed to see the new
    /// snapshot. Returns false if the wait expired, the caller disconnected, or
    /// the connection is not configured; in the first two cases the refresh is
    /// still running in the background.
    /// </summary>
    Task<bool> ForceRefreshAsync(string connection, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>
/// Polls each configured infrastructure connection on its own interval and
/// serves the results from memory, one cache per connection. Each connection
/// follows the same shape as <see cref="Terraform.ImageTagService"/>.
/// </summary>
public class InventoryService : BackgroundService, IInventoryService
{
    private readonly IInventoryProviderClient _client;
    private readonly ILogger<InventoryService> _logger;

    // Replaced, never mutated, so readers need no lock.
    private Dictionary<string, Connection> _connections = new(StringComparer.OrdinalIgnoreCase);

    private readonly object _connectionsLock = new();
    private CancellationToken _stoppingToken;
    private bool _running;

    public InventoryService(
        IOptionsMonitor<InfrastructureOptions> optionsMonitor,
        IInventoryProviderClient client,
        ILogger<InventoryService> logger)
    {
        _client = client;
        _logger = logger;

        Reconcile(optionsMonitor.CurrentValue);

        optionsMonitor.OnChange(x => Reconcile(optionsMonitor.CurrentValue));
    }

    public IReadOnlyList<InventoryConnectionInfo> GetConnections()
    {
        var connections = Ordered();
        var fallback = Default(connections);

        return connections
            .Select(x => new InventoryConnectionInfo
            {
                Name = x.Name,
                Provider = x.Options.Provider,
                Enabled = x.Options.Enabled,
                IsDefault = x == fallback,
                LastUpdated = x.Snapshot.LastUpdated
            })
            .ToArray();
    }

    public bool HasConnection(string connection) =>
        string.IsNullOrWhiteSpace(connection) || Find(connection) is not null;

    /// <summary>
    /// With no connections configured there is still an answer for a request that
    /// names none: the inventory is not set up, rather than a missing connection.
    /// </summary>
    public InventorySnapshot GetSnapshot(string connection = null) =>
        Find(connection)?.Snapshot ??
        (string.IsNullOrWhiteSpace(connection) ? InventorySnapshot.AllUnavailable(InventoryMessages.NotEnabled) : null);

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await Task.WhenAll(_connections.Values.Select(x => DoWork(x, cancellationToken)));
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Connection[] started;

        lock (_connectionsLock)
        {
            _stoppingToken = stoppingToken;
            _running = true;
            started = _connections.Values.ToArray();

            // StartAsync read the connections that existed then, so their loops
            // wait first. One added by a reload since then has not been read yet.
            foreach (var connection in started)
            {
                StartLoop(connection, readFirst: !connection.HasRead);
            }
        }

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }

        Task[] loops;

        lock (_connectionsLock)
        {
            loops = _connections.Values.Select(x => x.Loop).Where(x => x is not null).ToArray();
        }

        await Task.WhenAll(loops);
    }

    public void ForceRefresh(string connection = null)
    {
        Find(connection)?.Wake();
    }

    public Task<bool> ForceRefreshAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        ForceRefreshAsync(null, timeout, cancellationToken);

    public async Task<bool> ForceRefreshAsync(string connection, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var target = Find(connection);

        // With nothing configured there is nothing to refresh, which is done; a
        // named connection that does not exist is not.
        if (target is null)
            return string.IsNullOrWhiteSpace(connection);

        // Subscribe before triggering, so a fast refresh cannot complete between
        // the two and leave us waiting for the one after it.
        var completion = target.NextRefresh();

        target.Wake();

        // The provider call runs on the background service with the host's
        // stopping token. The request's token only bounds how long we wait here,
        // so a disconnected http caller never cancels the refresh itself.
        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        waitCts.CancelAfter(timeout);

        var finished = await Task.WhenAny(completion, Task.Delay(Timeout.Infinite, waitCts.Token));

        return finished == completion;
    }

    /// <summary>
    /// The connection by name, or the default one when none is named.
    /// </summary>
    private Connection Find(string connection)
    {
        if (string.IsNullOrWhiteSpace(connection))
            return Default(Ordered());

        return _connections.TryGetValue(connection.Trim(), out var found) ? found : null;
    }

    private Connection[] Ordered() => _connections.Values.OrderBy(x => x.Order).ToArray();

    /// <summary>
    /// The first enabled connection in configuration order, else the first one.
    /// </summary>
    private static Connection Default(Connection[] ordered) =>
        ordered.FirstOrDefault(x => x.Options.Enabled) ?? ordered.FirstOrDefault();

    /// <summary>
    /// Brings the connections in line with configuration. A connection that is
    /// still configured keeps its cache and is refreshed with the new settings, a
    /// new one starts reading straight away, and a removed one stops.
    /// </summary>
    private void Reconcile(InfrastructureOptions options)
    {
        options ??= new InfrastructureOptions();

        var desired = new Dictionary<string, (int Order, InfrastructureConnectionOptions Options)>(StringComparer.OrdinalIgnoreCase);
        var configured = options.Connections ?? [];

        for (var i = 0; i < configured.Count; i++)
        {
            var connection = configured[i];
            var name = connection?.Name?.Trim();

            if (connection is null)
                continue;

            if (string.IsNullOrEmpty(name))
            {
                _logger.LogWarning("Ignoring Infrastructure:Connections:{Index}: it has no Name", i);
                continue;
            }

            if (desired.ContainsKey(name))
            {
                _logger.LogWarning("Ignoring Infrastructure:Connections:{Index}: the name {Connection} is already used", i, name);
                continue;
            }

            desired[name] = (i, connection);
        }

        lock (_connectionsLock)
        {
            var next = new Dictionary<string, Connection>(StringComparer.OrdinalIgnoreCase);

            foreach (var (name, (order, connectionOptions)) in desired)
            {
                if (_connections.TryGetValue(name, out var existing))
                {
                    existing.Options = connectionOptions;
                    existing.Order = order;
                    existing.Wake();
                    next[name] = existing;
                    continue;
                }

                var added = new Connection(name, connectionOptions) { Order = order };
                next[name] = added;

                if (_running)
                    StartLoop(added, readFirst: true);
            }

            foreach (var (name, removed) in _connections)
            {
                if (!next.ContainsKey(name))
                    removed.Stop();
            }

            _connections = next;
        }
    }

    private void StartLoop(Connection connection, bool readFirst)
    {
        var token = connection.Start(_stoppingToken);
        connection.Loop = Task.Run(() => RunAsync(connection, readFirst, token), CancellationToken.None);
    }

    private async Task RunAsync(Connection connection, bool readFirst, CancellationToken cancellationToken)
    {
        if (readFirst)
            await DoWork(connection, cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            await connection.Wait(cancellationToken);

            // don't query the provider on the way out
            if (cancellationToken.IsCancellationRequested)
                break;

            await DoWork(connection, cancellationToken);
        }
    }

    private async Task DoWork(Connection connection, CancellationToken cancellationToken)
    {
        var options = connection.Options;

        try
        {
            if (!options.Enabled)
            {
                _logger.LogInformation("Infrastructure inventory connection {Connection} disabled, skipping inventory query", connection.Name);
                connection.Replace(InventorySnapshot.AllUnavailable(InventoryMessages.ConnectionNotEnabled), null);
                return;
            }

            _logger.LogInformation("Querying {Provider} inventory for connection {Connection} at {ProviderUrl}...",
                options.Provider, connection.Name, options.Url);
            connection.Replace(await _client.GetInventoryAsync(options, cancellationToken), options);
        }
        catch (OperationCanceledException)
        {
            // shutting down, or the connection was removed
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception getting infrastructure inventory for connection {Connection}.", connection.Name);

            var message = InventoryRedaction.Redact(
                $"Could not read the infrastructure inventory: {ex.Message}", options);

            connection.Replace(InventorySnapshot.AllUnavailable(message), options);
        }
        finally
        {
            connection.HasRead = true;

            // Snapshot is assigned before this fires, so anyone released by the
            // signal is guaranteed to observe the new value.
            connection.SignalRefreshComplete();
        }
    }

    /// <summary>
    /// One connection's settings, cache and refresh loop.
    /// </summary>
    private sealed class Connection(string name, InfrastructureConnectionOptions options)
    {
        // An already rewritten key is left alone.
        private static readonly Regex TopLevelKey = new("Infrastructure:(?!Connections:)", RegexOptions.CultureInvariant);

        public string Name { get; } = name;

        /// <summary>Position in Infrastructure:Connections, which also orders the list.</summary>
        public int Order { get; set; }

        public InfrastructureConnectionOptions Options { get; set; } = options;

        public InventorySnapshot Snapshot { get; private set; } = InventorySnapshot.AllUnavailable(InventoryMessages.ConnectionNotEnabled);

        // The provider and url Snapshot was read from. Null when it was not read.
        private string _snapshotTarget;

        public Task Loop { get; set; }

        public bool HasRead { get; set; }

        private TaskCompletionSource<bool> _wake = new();
        private CancellationTokenSource _stop;

        // Completed every time a refresh lands in the cache, then replaced. Lets a
        // caller wait for the *next* refresh without ever holding the provider call.
        private readonly object _refreshLock = new();
        private TaskCompletionSource<bool> _refreshCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken Start(CancellationToken stoppingToken)
        {
            _stop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            return _stop.Token;
        }

        public void Stop() => _stop?.Cancel();

        public void Wake() => _wake.TrySetCanceled();

        public Task NextRefresh()
        {
            lock (_refreshLock)
            {
                return _refreshCompletion.Task;
            }
        }

        public void SignalRefreshComplete()
        {
            TaskCompletionSource<bool> current;

            lock (_refreshLock)
            {
                current = _refreshCompletion;
                _refreshCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            current.TrySetResult(true);
        }

        public async Task Wait(CancellationToken cancellationToken)
        {
            var minutes = Options.RefreshMinutes > 0 ? Options.RefreshMinutes : 30;

            await Task.WhenAny(Task.Delay(TimeSpan.FromMinutes(minutes), cancellationToken), _wake.Task);

            if (_wake.Task.IsCanceled)
            {
                _wake = new TaskCompletionSource<bool>();
            }
        }

        /// <summary>
        /// Caches <paramref name="read"/>, keeping the last successful read of any
        /// category it is missing. Pass the options it was read with, or null for a
        /// snapshot that was not read; nothing is kept across a change of provider
        /// or url, since the old entries describe a different system.
        /// </summary>
        public void Replace(InventorySnapshot read, InfrastructureConnectionOptions readWith)
        {
            var target = readWith is null ? null : $"{readWith.Provider}|{readWith.Url}".ToLowerInvariant();

            var previous = target is not null && target == _snapshotTarget ? Snapshot : null;

            Snapshot = Scoped(read.KeepingLastGood(previous));
            _snapshotTarget = target;
        }

        /// <summary>
        /// Messages name a setting without its connection, e.g. "Set
        /// Infrastructure:Enabled to true". They are rewritten to this connection's
        /// own keys, so the message points at the setting that actually needs changing.
        /// </summary>
        public InventorySnapshot Scoped(InventorySnapshot snapshot)
        {
            if (snapshot is null)
                return snapshot;

            foreach (var category in new[] { snapshot.VmTemplates, snapshot.Isos, snapshot.Networks, snapshot.Datastores, snapshot.ContentLibrary, snapshot.Hosts })
            {
                if (category?.Error is not null)
                    category.Error = TopLevelKey.Replace(category.Error, $"Infrastructure:Connections:{Order}:");
            }

            return snapshot;
        }
    }
}
