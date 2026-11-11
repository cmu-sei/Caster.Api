// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

    /// <summary>Whether inventory is enabled for the connection (its Inventory:Enabled).</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// True for the connection used when a request names none.
    /// </summary>
    public bool IsDefault { get; set; }

    public DateTime? LastUpdated { get; set; }

    /// <summary>The kinds of object the connection reads, from its Inventory:Resources.</summary>
    public InventoryCategory[] Resources { get; set; } = [];

    /// <summary>Why the connection's settings can't be used, or null when they can.</summary>
    public string Error { get; set; }
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
                Enabled = IsEnabled(x.Options),
                IsDefault = x == fallback,
                LastUpdated = x.Snapshot.LastUpdated,
                Resources = InventoryFilters.Enabled(x.Options),
                Error = x.Misconfiguration
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

        // A caller that has already gone gets an answer straight away. The refresh it
        // asked for still runs; only the wait is skipped, so a refresh that happens to
        // finish first can't make an abandoned request look completed.
        if (cancellationToken.IsCancellationRequested)
            return false;

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
        ordered.FirstOrDefault(x => IsEnabled(x.Options)) ?? ordered.FirstOrDefault();

    private static bool IsEnabled(InfrastructureConnectionOptions options) => options.Inventory?.Enabled == true;

    /// <summary>
    /// Brings the connections in line with configuration. A connection that is
    /// still configured is refreshed with the new settings, keeping its cache only if
    /// nothing that changes what it lists has changed. A new one starts reading
    /// straight away, and a removed one stops.
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
                    existing.Order = order;
                    existing.Configure(connectionOptions, Validate(name, connectionOptions, order));
                    existing.Wake();
                    next[name] = existing;
                    continue;
                }

                var added = new Connection(name, connectionOptions) { Order = order };
                added.Configure(connectionOptions, Validate(name, connectionOptions, order));
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

    /// <summary>
    /// Why a connection's settings can't be used, naming its own keys, or null. Logged once
    /// per configuration, since the connection isn't read until it's fixed.
    /// </summary>
    private string Validate(string name, InfrastructureConnectionOptions options, int order)
    {
        var problem = InventoryFilters.Validate(options);

        if (problem is null)
            return null;

        var scoped = Connection.Scope(InventoryMessages.Misconfigured(problem), order);
        _logger.LogWarning("Infrastructure connection {Connection} is misconfigured: {Problem}", name, scoped);

        return scoped;
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
        // Everything this read uses is taken together, so a reconfiguration while it
        // runs can't mix old and new settings, and its result is dropped.
        var (options, revision, misconfiguration) = connection.Current();
        var published = true;

        try
        {
            if (!IsEnabled(options))
            {
                _logger.LogInformation("Infrastructure inventory connection {Connection} disabled, skipping inventory query", connection.Name);
                published = connection.Replace(InventorySnapshot.AllUnavailable(InventoryMessages.ConnectionNotEnabled), null, revision);
                return;
            }

            if (misconfiguration is not null)
            {
                published = connection.Replace(InventorySnapshot.AllUnavailable(misconfiguration), null, revision);
                return;
            }

            _logger.LogInformation("Querying {Provider} inventory for connection {Connection} at {ProviderUrl}...",
                options.Provider, connection.Name, options.Url);

            var read = await _client.GetInventoryAsync(options, cancellationToken);
            published = connection.Replace(InventoryFilters.Apply(read, options), options, revision);

            if (!published)
                _logger.LogInformation("Discarding the inventory read for connection {Connection}: its settings changed while it ran", connection.Name);
        }
        catch (OperationCanceledException)
        {
            // shutting down, or the connection was removed
        }
        catch (Exception ex)
        {
            // The exception itself can carry the credentials, so only its redacted message is logged.
            var message = InventoryRedaction.Redact(
                $"Could not read the infrastructure inventory: {ex.Message}", options);

            _logger.LogError("Exception getting infrastructure inventory for connection {Connection}: {Error}",
                connection.Name, InventoryRedaction.Describe(ex, options));

            published = connection.Replace(InventorySnapshot.AllUnavailable(message), options, revision);
        }
        finally
        {
            connection.HasRead = true;

            // Snapshot is assigned before this fires, so anyone released by the
            // signal is guaranteed to observe the new value. A discarded read
            // doesn't count: the reconfiguration woke the loop for another.
            if (published)
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

        private readonly object _settingsLock = new();

        public InfrastructureConnectionOptions Options { get; private set; } = options;

        /// <summary>Why <see cref="Options"/> can't be used, naming this connection's keys, or null.</summary>
        public string Misconfiguration { get; private set; }

        /// <summary>
        /// Goes up whenever a setting that changes what the connection lists changes. A
        /// read started under an earlier revision is discarded rather than cached.
        /// </summary>
        public long Revision { get; private set; }

        private string _fingerprint;

        public InventorySnapshot Snapshot { get; private set; } = InventorySnapshot.AllUnavailable(InventoryMessages.ConnectionNotEnabled);

        // Whether Snapshot holds a read worth keeping categories from.
        private bool _snapshotKeepable;

        /// <summary>
        /// Applies new settings. When anything that changes what is listed has changed
        /// (provider, url, credentials, Datacenters, IsoLocations or the Inventory
        /// settings other than RefreshMinutes), the cache is cleared and the revision
        /// goes up, so nothing read under the old settings is shown or kept.
        /// </summary>
        public void Configure(InfrastructureConnectionOptions next, string misconfiguration)
        {
            var fingerprint = Fingerprint(next);

            lock (_settingsLock)
            {
                Options = next;
                Misconfiguration = misconfiguration;

                if (fingerprint == _fingerprint)
                    return;

                var first = _fingerprint is null;
                _fingerprint = fingerprint;
                Revision++;

                if (!first)
                {
                    Snapshot = InventorySnapshot.AllUnavailable(InventoryMessages.NotLoaded);
                    _snapshotKeepable = false;
                }
            }
        }

        public (InfrastructureConnectionOptions Options, long Revision, string Misconfiguration) Current()
        {
            lock (_settingsLock)
            {
                return (Options, Revision, Misconfiguration);
            }
        }

        /// <summary>
        /// Everything that changes what the connection lists, hashed so the credentials
        /// aren't kept in another readable copy. RefreshMinutes only changes when.
        /// </summary>
        internal static string Fingerprint(InfrastructureConnectionOptions options)
        {
            var visible = new
            {
                Provider = options.Provider?.Trim().ToLowerInvariant(),
                Url = options.Url?.Trim().ToLowerInvariant(),
                options.Username,
                options.Password,
                options.ApiToken,
                options.InsecureSkipVerify,
                options.Datacenters,
                options.IsoLocations,
                options.Inventory?.Enabled,
                options.Inventory?.Resources
            };

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(visible))));
        }

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
            var configured = Options.Inventory?.RefreshMinutes ?? 0;
            var minutes = configured > 0 ? configured : 30;

            await Task.WhenAny(Task.Delay(TimeSpan.FromMinutes(minutes), cancellationToken), _wake.Task);

            if (_wake.Task.IsCanceled)
            {
                _wake = new TaskCompletionSource<bool>();
            }
        }

        /// <summary>
        /// Caches <paramref name="read"/>, keeping the last successful read of any
        /// category it is missing. Pass the options it was read with, or null for a
        /// snapshot that was not read, which keeps nothing. Returns false, caching
        /// nothing, when the settings changed after the read started.
        /// </summary>
        public bool Replace(InventorySnapshot read, InfrastructureConnectionOptions readWith, long revision)
        {
            lock (_settingsLock)
            {
                if (revision != Revision)
                    return false;

                var previous = readWith is not null && _snapshotKeepable ? Snapshot : null;

                Snapshot = Scoped(read.KeepingLastGood(previous));
                _snapshotKeepable = readWith is not null;
                return true;
            }
        }

        /// <summary>
        /// Messages name a setting without its connection, e.g. "Set
        /// Infrastructure:Inventory:Enabled to true". They are rewritten to this connection's
        /// own keys, so the message points at the setting that actually needs changing.
        /// </summary>
        public InventorySnapshot Scoped(InventorySnapshot snapshot)
        {
            if (snapshot is null)
                return snapshot;

            foreach (var category in new[] { snapshot.VmTemplates, snapshot.Isos, snapshot.Networks, snapshot.Datastores, snapshot.ContentLibrary, snapshot.Hosts })
            {
                if (category?.Error is not null)
                    category.Error = Scope(category.Error, Order);
            }

            return snapshot;
        }

        /// <summary>Rewrites "Infrastructure:X" to "Infrastructure:Connections:{order}:X".</summary>
        public static string Scope(string message, int order) =>
            message is null ? null : TopLevelKey.Replace(message, $"Infrastructure:Connections:{order}:");
    }
}
