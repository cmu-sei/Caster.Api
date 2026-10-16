// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Services.Inventory;
using Caster.Api.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Caster.Api.Tests.Domain.Services.Inventory
{
    [Trait("Category", "Unit")]
    public class InventoryServiceTests
    {
        private const string Password = "sup3r-s3cret-pw";

        [Fact]
        public async Task Disabled_ReportsNotEnabledAndNeverContactsTheProvider()
        {
            var client = new FakeProviderClient(_ => Loaded());
            var sut = Build(new InfrastructureOptions(), client);

            await Run(sut);

            var snapshot = sut.GetSnapshot();

            Assert.Equal(0, client.Calls);
            Assert.Null(snapshot.LastUpdated);
            Assert.False(snapshot.VmTemplates.Available);
            Assert.False(snapshot.Isos.Available);
            Assert.False(snapshot.Networks.Available);
            Assert.False(snapshot.Datastores.Available);
            Assert.Equal(InventoryMessages.NotEnabled, snapshot.VmTemplates.Error);
        }

        [Fact]
        public void Disabled_IsTheDefault()
        {
            Assert.False(new InfrastructureOptions().Enabled);
            Assert.Equal(30, new InfrastructureOptions().RefreshMinutes);
            Assert.Equal("vsphere", new InfrastructureOptions().Provider);

            // and nothing is served until a read succeeds
            var sut = Build(new InfrastructureOptions(), new FakeProviderClient(_ => Loaded()));
            Assert.False(sut.GetSnapshot().VmTemplates.Available);
        }

        [Fact]
        public async Task Enabled_ServesTheProviderResultFromMemory()
        {
            var client = new FakeProviderClient(_ => Loaded());
            var sut = Build(EnabledOptions(), client);

            await Run(sut);

            var snapshot = sut.GetSnapshot();

            Assert.Equal(1, client.Calls);
            Assert.NotNull(snapshot.LastUpdated);
            Assert.Equal(DateTimeKind.Utc, snapshot.LastUpdated.Value.Kind);

            Assert.True(snapshot.VmTemplates.Available);
            var entry = Assert.Single(snapshot.VmTemplates.Entries);
            Assert.Equal("vm-2", entry.Id);
            Assert.Equal("win2019-template", entry.Name);

            // repeated reads are served from the cache, not the provider
            sut.GetSnapshot();
            sut.GetSnapshot();
            Assert.Equal(1, client.Calls);
        }

        [Fact]
        public async Task Enabled_PassesTheConfiguredConnectionToTheProvider()
        {
            var client = new FakeProviderClient(_ => Loaded());
            var options = EnabledOptions();
            var sut = Build(options, client);

            await Run(sut);

            Assert.Equal(options.Url, client.LastOptions.Url);
            Assert.Equal(options.Username, client.LastOptions.Username);
        }

        [Fact]
        public async Task ProviderThrows_ReportsUnavailableAndRedactsThePassword()
        {
            var client = new FakeProviderClient(_ => throw new InvalidOperationException($"login failed for {Password}"));
            var sut = Build(EnabledOptions(), client);

            await Run(sut);

            var snapshot = sut.GetSnapshot();

            Assert.False(snapshot.VmTemplates.Available);
            Assert.False(snapshot.Isos.Available);
            Assert.False(snapshot.Networks.Available);
            Assert.False(snapshot.Datastores.Available);
            Assert.DoesNotContain(Password, snapshot.VmTemplates.Error);
            Assert.Contains("***", snapshot.VmTemplates.Error);
        }

        [Fact]
        public async Task ForceRefresh_WakesTheBackgroundLoop()
        {
            var secondCall = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;

            var client = new FakeProviderClient(_ =>
            {
                if (Interlocked.Increment(ref calls) == 2)
                {
                    secondCall.TrySetResult(true);
                }

                return Loaded();
            });

            // a long interval, so the only thing that can trigger a second read is ForceRefresh
            var options = EnabledOptions();
            options.RefreshMinutes = 600;

            var sut = Build(options, client);

            try
            {
                await sut.StartAsync(CancellationToken.None);
                Assert.Equal(1, client.Calls);

                sut.ForceRefresh();

                var completed = await Task.WhenAny(secondCall.Task, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
                Assert.Same(secondCall.Task, completed);
                Assert.True(client.Calls >= 2);
            }
            finally
            {
                await sut.StopAsync(CancellationToken.None);
            }
        }

        [Fact]
        public async Task ForceRefreshAsync_WaitsForTheNewSnapshotToLand()
        {
            var calls = 0;
            var client = new FakeProviderClient(_ =>
            {
                var stamp = new DateTime(2026, 1, 1, 0, Interlocked.Increment(ref calls), 0, DateTimeKind.Utc);
                var snapshot = Loaded();
                snapshot.LastUpdated = stamp;
                return snapshot;
            });

            var options = EnabledOptions();
            options.RefreshMinutes = 600;

            var sut = Build(options, client);

            try
            {
                await sut.StartAsync(CancellationToken.None);
                var before = sut.GetSnapshot().LastUpdated;

                var completed = await sut.ForceRefreshAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

                Assert.True(completed);

                // the point of the bounded wait: an immediate read sees the new data
                Assert.NotEqual(before, sut.GetSnapshot().LastUpdated);
                Assert.True(client.Calls >= 2);
            }
            finally
            {
                await sut.StopAsync(CancellationToken.None);
            }
        }

        [Fact]
        public async Task ForceRefreshAsync_ReturnsFalseWhenTheWaitExpires()
        {
            // the background loop is never started, so nothing can ever signal
            var client = new FakeProviderClient(_ => Loaded());
            var sut = Build(EnabledOptions(), client);

            var completed = await sut.ForceRefreshAsync(TimeSpan.FromMilliseconds(250), CancellationToken.None);

            Assert.False(completed);
        }

        [Fact]
        public async Task ForceRefreshAsync_CallerDisconnect_DoesNotCancelTheProviderCall()
        {
            var providerToken = CancellationToken.None;
            var client = new FakeProviderClient(_ => Loaded());

            var options = EnabledOptions();
            options.RefreshMinutes = 600;

            var sut = Build(options, client);
            client.OnCall = token => providerToken = token;

            try
            {
                await sut.StartAsync(CancellationToken.None);

                using var disconnected = new CancellationTokenSource();
                await disconnected.CancelAsync();

                // an already cancelled request token must not throw and must not
                // poison the background refresh
                var completed = await sut.ForceRefreshAsync(TimeSpan.FromSeconds(10), disconnected.Token);

                Assert.False(completed);
                Assert.False(providerToken.IsCancellationRequested);
            }
            finally
            {
                await sut.StopAsync(CancellationToken.None);
            }
        }

        [Fact]
        public async Task ForceRefreshAsync_DisabledProvider_StillCompletes()
        {
            var client = new FakeProviderClient(_ => Loaded());
            var sut = Build(new InfrastructureOptions(), client);

            try
            {
                await sut.StartAsync(CancellationToken.None);

                var completed = await sut.ForceRefreshAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

                Assert.True(completed);
                Assert.Equal(0, client.Calls);
                Assert.Null(sut.GetSnapshot().LastUpdated);
            }
            finally
            {
                await sut.StopAsync(CancellationToken.None);
            }
        }

        #region Connections

        [Fact]
        public async Task NamedConnection_IsReadAndCachedOnItsOwn()
        {
            var client = new FakeProviderClient(ReadFrom);
            var sut = Build(TwoConnections(), client);

            await Run(sut);

            Assert.Equal("https://vcenter.test", Assert.Single(sut.GetSnapshot().VmTemplates.Entries).Name);
            Assert.Equal("https://pve2.test:8006", Assert.Single(sut.GetSnapshot("lab2").VmTemplates.Entries).Name);

            // null, empty and "default" all name the top-level settings
            Assert.Same(sut.GetSnapshot(), sut.GetSnapshot(null));
            Assert.Same(sut.GetSnapshot(), sut.GetSnapshot(""));
            Assert.Same(sut.GetSnapshot(), sut.GetSnapshot("default"));

            // names are case insensitive, like the configuration keys they come from
            Assert.Same(sut.GetSnapshot("lab2"), sut.GetSnapshot(" LAB2 "));

            Assert.Equal(1, client.CallsTo("https://vcenter.test"));
            Assert.Equal(1, client.CallsTo("https://pve2.test:8006"));
        }

        [Fact]
        public async Task UnknownConnection_HasNoSnapshot()
        {
            var sut = Build(TwoConnections(), new FakeProviderClient(ReadFrom));

            await Run(sut);

            Assert.False(sut.HasConnection("lab3"));
            Assert.Null(sut.GetSnapshot("lab3"));
            Assert.False(await sut.ForceRefreshAsync("lab3", TimeSpan.FromSeconds(1), CancellationToken.None));

            Assert.True(sut.HasConnection(null));
            Assert.True(sut.HasConnection("lab2"));
        }

        [Fact]
        public async Task NamedConnection_InheritsNothingFromTheDefault()
        {
            var seen = new System.Collections.Concurrent.ConcurrentBag<InfrastructureConnectionOptions>();
            var sut = Build(TwoConnections(), new FakeProviderClient(x =>
            {
                seen.Add(x);
                return ReadFrom(x);
            }));

            await Run(sut);

            var lab2 = Assert.Single(seen, x => x.Url == "https://pve2.test:8006");

            // the default connection's vSphere account never reaches the lab2 provider
            Assert.Null(lab2.Username);
            Assert.Null(lab2.Password);
            Assert.Equal("proxmox", lab2.Provider);
        }

        [Fact]
        public async Task NamedConnection_MessagesNameItsOwnSettings()
        {
            var options = TwoConnections();
            options.Connections["lab2"].Enabled = false;

            var sut = Build(options, new FakeProviderClient(ReadFrom));

            await Run(sut);

            var error = sut.GetSnapshot("lab2").VmTemplates.Error;

            Assert.Contains("Set Infrastructure:Connections:lab2:Enabled to true", error);
            Assert.DoesNotContain("Set Infrastructure:Enabled", error);

            // and the default connection's messages are untouched
            options.Enabled = false;
            var defaultOnly = Build(options, new FakeProviderClient(ReadFrom));
            await Run(defaultOnly);
            Assert.Equal(InventoryMessages.NotEnabled, defaultOnly.GetSnapshot().VmTemplates.Error);
        }

        [Fact]
        public async Task NamedConnection_RewritesAProviderMessageOnce()
        {
            var options = TwoConnections();

            // the same snapshot object comes back on every read
            var shared = InventorySnapshot.AllUnavailable(InventoryMessages.ProxmoxApiTokenMalformed);
            var client = new FakeProviderClient(x => x.Url == "https://pve2.test:8006" ? shared : ReadFrom(x));
            var sut = Build(options, client);

            try
            {
                await sut.StartAsync(CancellationToken.None);
                Assert.True(await sut.ForceRefreshAsync("lab2", TimeSpan.FromSeconds(10), CancellationToken.None));
            }
            finally
            {
                await sut.StopAsync(CancellationToken.None);
            }

            Assert.StartsWith("Infrastructure:Connections:lab2:ApiToken is not", sut.GetSnapshot("lab2").Isos.Error);
        }

        [Fact]
        public async Task NamedConnection_ProviderThrows_RedactsThatConnectionsToken()
        {
            var client = new FakeProviderClient(x => x.Url == "https://pve2.test:8006"
                ? throw new InvalidOperationException($"bad token {Lab2Token}")
                : ReadFrom(x));
            var sut = Build(TwoConnections(), client);

            await Run(sut);

            var error = sut.GetSnapshot("lab2").VmTemplates.Error;

            Assert.DoesNotContain(Lab2Token, error);
            Assert.DoesNotContain(Lab2Token[(Lab2Token.IndexOf('=') + 1)..], error);
            Assert.Contains("***", error);

            // one connection failing does not touch another
            Assert.True(sut.GetSnapshot().VmTemplates.Available);
        }

        [Fact]
        public async Task ForceRefreshAsync_RefreshesOnlyTheNamedConnection()
        {
            var client = new FakeProviderClient(ReadFrom);
            var sut = Build(TwoConnections(), client);

            try
            {
                await sut.StartAsync(CancellationToken.None);

                Assert.True(await sut.ForceRefreshAsync("lab2", TimeSpan.FromSeconds(10), CancellationToken.None));
                Assert.True(await sut.ForceRefreshAsync("lab2", TimeSpan.FromSeconds(10), CancellationToken.None));

                Assert.Equal(3, client.CallsTo("https://pve2.test:8006"));
                Assert.Equal(1, client.CallsTo("https://vcenter.test"));
            }
            finally
            {
                await sut.StopAsync(CancellationToken.None);
            }
        }

        [Fact]
        public void GetConnections_ListsTheDefaultFirstWithoutUrlsOrCredentials()
        {
            var options = TwoConnections();
            options.Connections["alpha"] = new InfrastructureConnectionOptions { Provider = "vsphere" };

            var sut = Build(options, new FakeProviderClient(ReadFrom));

            var connections = sut.GetConnections();

            Assert.Equal(["default", "alpha", "lab2"], connections.Select(x => x.Name));
            Assert.True(connections[0].IsDefault);
            Assert.False(connections[1].Enabled);
            Assert.Equal("proxmox", connections[2].Provider);

            // the listing type has nowhere to put a url or a secret
            var properties = typeof(InventoryConnectionInfo).GetProperties().Select(x => x.Name).ToArray();
            Assert.Equal(["Name", "Provider", "Enabled", "IsDefault", "LastUpdated"], properties);
        }

        [Fact]
        public void ConnectionNamedDefault_IsIgnored()
        {
            var options = EnabledOptions();
            options.Connections["Default"] = new InfrastructureConnectionOptions
            {
                Enabled = true,
                Provider = "proxmox",
                Url = "https://impostor.test"
            };

            var sut = Build(options, new FakeProviderClient(ReadFrom));

            var connection = Assert.Single(sut.GetConnections());
            Assert.Equal("vsphere", connection.Provider);
        }

        [Fact]
        public async Task OptionsChange_AddsAndRemovesConnections()
        {
            var client = new FakeProviderClient(ReadFrom);
            var monitor = new TestOptionsMonitor<InfrastructureOptions>(EnabledOptions());
            var sut = Build(monitor, client);

            try
            {
                await sut.StartAsync(CancellationToken.None);
                Assert.Null(sut.GetSnapshot("lab2"));

                monitor.Set(TwoConnections());

                // a new connection reads straight away, without waiting out its interval
                await WaitFor(() => sut.GetSnapshot("lab2")?.VmTemplates.Available == true);
                Assert.Equal("https://pve2.test:8006", Assert.Single(sut.GetSnapshot("lab2").VmTemplates.Entries).Name);

                monitor.Set(EnabledOptions());

                Assert.Null(sut.GetSnapshot("lab2"));
                Assert.Equal(["default"], sut.GetConnections().Select(x => x.Name));
            }
            finally
            {
                await sut.StopAsync(CancellationToken.None);
            }
        }

        private static async Task WaitFor(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);

            while (!condition())
            {
                Assert.True(DateTime.UtcNow < deadline, "timed out waiting for the condition");
                await Task.Delay(20);
            }
        }

        #endregion

        #region Helpers

        private static InfrastructureOptions EnabledOptions() => new()
        {
            Enabled = true,
            Provider = "vsphere",
            Url = "https://vcenter.test",
            Username = "caster-ro@vsphere.local",
            Password = Password,
            RefreshMinutes = 600
        };

        private static InventorySnapshot Loaded() => new()
        {
            LastUpdated = DateTime.UtcNow,
            VmTemplates = InventoryCategoryResult.Ok([
                new InventoryEntry { Id = "vm-2", Name = "win2019-template", Path = "win2019-template" }
            ]),
            Networks = InventoryCategoryResult.Ok([
                new InventoryEntry { Id = "network-11", Name = "vlan-200", Path = "vlan-200" }
            ]),
            Datastores = InventoryCategoryResult.Ok([]),
            Isos = InventoryCategoryResult.Unavailable(InventoryMessages.IsoNotSupported)
        };

        private static InventoryService Build(InfrastructureOptions options, IInventoryProviderClient client) =>
            Build(new TestOptionsMonitor<InfrastructureOptions>(options), client);

        private static InventoryService Build(IOptionsMonitor<InfrastructureOptions> monitor, IInventoryProviderClient client) =>
            new(monitor, client, Substitute.For<ILogger<InventoryService>>());

        private const string Lab2Token = "caster@pve!lab2=9f1c2d3e-0000-4000-8000-00000000abcd";

        /// <summary>
        /// The default connection plus an enabled Proxmox connection named lab2,
        /// with nothing in common, so a test can tell which settings a read used.
        /// </summary>
        private static InfrastructureOptions TwoConnections()
        {
            var options = EnabledOptions();

            options.Connections["lab2"] = new InfrastructureConnectionOptions
            {
                Enabled = true,
                Provider = "proxmox",
                Url = "https://pve2.test:8006",
                ApiToken = Lab2Token,
                RefreshMinutes = 600
            };

            return options;
        }

        /// <summary>
        /// One template named after the url it was read from.
        /// </summary>
        private static InventorySnapshot ReadFrom(InfrastructureConnectionOptions options) => new()
        {
            LastUpdated = DateTime.UtcNow,
            VmTemplates = InventoryCategoryResult.Ok([
                new InventoryEntry { Id = options.Url, Name = options.Url, Path = options.Url }
            ]),
            Networks = InventoryCategoryResult.Ok([]),
            Datastores = InventoryCategoryResult.Ok([]),
            Isos = InventoryCategoryResult.Ok([])
        };

        /// <summary>
        /// Runs one refresh cycle and shuts the background loop back down.
        /// </summary>
        private static async Task Run(InventoryService sut)
        {
            try
            {
                await sut.StartAsync(CancellationToken.None);
            }
            finally
            {
                await sut.StopAsync(CancellationToken.None);
            }
        }

        private sealed class FakeProviderClient(Func<InfrastructureConnectionOptions, InventorySnapshot> responder) : IInventoryProviderClient
        {
            private int _calls;

            public int Calls => _calls;
            public InfrastructureConnectionOptions LastOptions { get; private set; }

            /// <summary>
            /// Every read, in order, by the url it was made against.
            /// </summary>
            public System.Collections.Concurrent.ConcurrentQueue<string> Urls { get; } = new();

            public int CallsTo(string url) => Urls.Count(x => x == url);

            /// <summary>
            /// Lets a test observe the token the provider call actually receives.
            /// </summary>
            public Action<CancellationToken> OnCall { get; set; }

            public Task<InventorySnapshot> GetInventoryAsync(InfrastructureConnectionOptions options, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _calls);
                LastOptions = options;
                Urls.Enqueue(options.Url);
                OnCall?.Invoke(cancellationToken);
                return Task.FromResult(responder(options));
            }
        }

        private sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
        {
            private Action<T, string> _listener;

            public T CurrentValue => value;

            public T Get(string name) => value;

            public IDisposable OnChange(Action<T, string> listener)
            {
                _listener = listener;
                return new NoopDisposable();
            }

            /// <summary>
            /// Replaces the value and notifies the listener, as a config reload does.
            /// </summary>
            public void Set(T next)
            {
                value = next;
                _listener?.Invoke(next, null);
            }

            private sealed class NoopDisposable : IDisposable
            {
                public void Dispose() { }
            }
        }

        #endregion
    }
}
