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
            Assert.Empty(new InfrastructureOptions().Connections);
            Assert.False(new InfrastructureConnectionOptions().Inventory.Enabled);
            Assert.Equal(30, new InfrastructureConnectionOptions().Inventory.RefreshMinutes);
            Assert.Equal("vsphere", new InfrastructureConnectionOptions().Provider);

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

            Assert.Equal(options.Connections[0].Url, client.LastOptions.Url);
            Assert.Equal(options.Connections[0].Username, client.LastOptions.Username);
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
            options.Connections[0].Inventory.RefreshMinutes = 600;

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
            options.Connections[0].Inventory.RefreshMinutes = 600;

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
            options.Connections[0].Inventory.RefreshMinutes = 600;

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

        #region Keeping the last good read

        private static readonly DateTime FirstRead = new(2026, 10, 24, 9, 30, 0, DateTimeKind.Utc);
        private static readonly DateTime ThirdRead = new(2026, 10, 24, 10, 30, 0, DateTimeKind.Utc);

        private const string Refused = "Could not connect to Proxmox at https://pve.test:8006: Connection refused";

        [Fact]
        public async Task ProviderDown_AfterAGoodRead_KeepsThatReadAsStale()
        {
            var sut = Build(EnabledOptions(), Reads(
                () => LoadedAt(FirstRead),
                () => InventorySnapshot.AllUnavailable(Refused)));

            await RunReads(sut, refreshes: 1);

            var snapshot = sut.GetSnapshot();
            var templates = snapshot.VmTemplates;

            Assert.True(templates.Available);
            Assert.True(templates.Stale);
            Assert.Equal("win2019-template", Assert.Single(templates.Entries).Name);
            Assert.Equal(FirstRead, templates.LastUpdated);
            Assert.Equal(Refused, templates.Error);

            Assert.True(snapshot.Networks.Stale);
            Assert.Equal(FirstRead, snapshot.LastUpdated);
            Assert.Equal(FirstRead, Assert.Single(sut.GetConnections()).LastUpdated);

            // never read, so there is nothing to keep
            Assert.False(snapshot.Isos.Available);
            Assert.False(snapshot.Isos.Stale);
            Assert.Equal(Refused, snapshot.Isos.Error);
        }

        [Fact]
        public async Task ContentLibraryDown_AfterAGoodRead_KeepsItAsStale()
        {
            var library = new InventoryEntry { Id = "item-1", Name = "ubuntu-2404", Path = "Golden/ubuntu-2404" };
            var sut = Build(EnabledOptions(), Reads(
                () =>
                {
                    var read = LoadedAt(FirstRead);
                    read.ContentLibrary = InventoryCategoryResult.Ok([library]);
                    return read;
                },
                () =>
                {
                    var read = LoadedAt(ThirdRead);
                    read.ContentLibrary = InventoryCategoryResult.Unavailable("vCenter returned 403 Forbidden when reading content libraries.");
                    return read;
                }));

            await RunReads(sut, refreshes: 1);

            var kept = sut.GetSnapshot().ContentLibrary;
            Assert.True(kept.Available);
            Assert.True(kept.Stale);
            Assert.Equal(FirstRead, kept.LastUpdated);
            Assert.Equal("Golden/ubuntu-2404", Assert.Single(kept.Entries).Path);
            Assert.Contains("403", kept.Error);
        }

        [Fact]
        public async Task ANotApplicableCategory_IsNeverKeptAsStale()
        {
            var sut = Build(EnabledOptions(), Reads(() =>
            {
                var read = LoadedAt(FirstRead);
                read.ContentLibrary = InventoryCategoryResult.NotApplicable(InventoryMessages.ContentLibraryNotOnProxmox);
                return read;
            }));

            await RunReads(sut, refreshes: 1);

            var library = sut.GetSnapshot().ContentLibrary;
            Assert.False(library.Applies);
            Assert.False(library.Available);
            Assert.False(library.Stale);
        }

        [Fact]
        public async Task EveryCategoryFails_AfterConnecting_IsNotANewerRead()
        {
            var sut = Build(EnabledOptions(), Reads(
                () => LoadedAt(FirstRead),
                () =>
                {
                    // connected, so the provider stamped the read, then nothing came back
                    var read = InventorySnapshot.AllUnavailable("Proxmox returned 500 InternalServerError.");
                    read.LastUpdated = ThirdRead;
                    return read;
                }));

            await RunReads(sut, refreshes: 1);

            Assert.Equal(FirstRead, sut.GetSnapshot().LastUpdated);
            Assert.True(sut.GetSnapshot().VmTemplates.Stale);
        }

        [Fact]
        public async Task ProviderThrows_AfterAGoodRead_KeepsThatReadAsStale()
        {
            var sut = Build(EnabledOptions(), Reads(
                () => LoadedAt(FirstRead),
                () => throw new InvalidOperationException($"login failed for {Password}")));

            await RunReads(sut, refreshes: 1);

            var templates = sut.GetSnapshot().VmTemplates;

            Assert.True(templates.Stale);
            Assert.Equal("win2019-template", Assert.Single(templates.Entries).Name);
            Assert.DoesNotContain(Password, templates.Error);
            Assert.Contains("***", templates.Error);
        }

        [Fact]
        public async Task PartialRead_KeepsOnlyTheCategoryThatFailed()
        {
            var sut = Build(EnabledOptions(), Reads(
                () => LoadedAt(FirstRead),
                () =>
                {
                    var read = LoadedAt(ThirdRead);
                    read.VmTemplates = InventoryCategoryResult.Ok([new InventoryEntry { Id = "vm-3", Name = "win2022-template" }]);
                    read.Networks = InventoryCategoryResult.Unavailable("Proxmox returned 500 InternalServerError when reading networks.");
                    return read;
                }));

            await RunReads(sut, refreshes: 1);

            var snapshot = sut.GetSnapshot();

            Assert.False(snapshot.VmTemplates.Stale);
            Assert.Equal("win2022-template", Assert.Single(snapshot.VmTemplates.Entries).Name);
            Assert.Equal(ThirdRead, snapshot.VmTemplates.LastUpdated);

            Assert.True(snapshot.Networks.Stale);
            Assert.Equal("vlan-200", Assert.Single(snapshot.Networks.Entries).Name);
            Assert.Equal(FirstRead, snapshot.Networks.LastUpdated);
        }

        [Fact]
        public async Task ProviderBack_ReplacesTheKeptRead()
        {
            var sut = Build(EnabledOptions(), Reads(
                () => LoadedAt(FirstRead),
                () => InventorySnapshot.AllUnavailable(Refused),
                () => LoadedAt(ThirdRead)));

            await RunReads(sut, refreshes: 2);

            var templates = sut.GetSnapshot().VmTemplates;

            Assert.False(templates.Stale);
            Assert.Null(templates.Error);
            Assert.Equal(ThirdRead, templates.LastUpdated);
        }

        [Fact]
        public async Task UrlChange_DoesNotKeepTheOtherSystemsRead()
        {
            var monitor = new TestOptionsMonitor<InfrastructureOptions>(EnabledOptions());
            var sut = Build(monitor, new FakeProviderClient(options => options.Url == EnabledOptions().Connections[0].Url
                ? LoadedAt(FirstRead)
                : InventorySnapshot.AllUnavailable(Refused)));

            await RunReads(sut, refreshes: 1, beforeRefresh: () =>
            {
                var moved = EnabledOptions();
                moved.Connections[0].Url = "https://vcenter2.test";
                monitor.Set(moved);
            });

            var snapshot = sut.GetSnapshot();

            Assert.False(snapshot.VmTemplates.Available);
            Assert.Empty(snapshot.VmTemplates.Entries);
            Assert.Null(snapshot.LastUpdated);
        }

        [Fact]
        public async Task Disabled_AfterAGoodRead_DropsIt()
        {
            var monitor = new TestOptionsMonitor<InfrastructureOptions>(EnabledOptions());
            var sut = Build(monitor, new FakeProviderClient(_ => LoadedAt(FirstRead)));

            await RunReads(sut, refreshes: 1, beforeRefresh: () =>
            {
                var disabled = EnabledOptions();
                disabled.Connections[0].Inventory.Enabled = false;
                monitor.Set(disabled);
            });

            var templates = sut.GetSnapshot().VmTemplates;

            Assert.False(templates.Available);
            Assert.False(templates.Stale);
            Assert.Contains("Set Infrastructure:Connections:0:Inventory:Enabled to true", templates.Error);
        }

        /// <summary>
        /// Answers each read with the next responder in turn, then repeats the last.
        /// </summary>
        private static FakeProviderClient Reads(params Func<InventorySnapshot>[] reads)
        {
            var calls = -1;
            return new FakeProviderClient(_ => reads[Math.Min(Interlocked.Increment(ref calls), reads.Length - 1)]());
        }

        private static InventorySnapshot LoadedAt(DateTime stamp)
        {
            var snapshot = Loaded();
            snapshot.LastUpdated = stamp;
            return snapshot;
        }

        /// <summary>
        /// Starts the service, which reads once, then forces <paramref name="refreshes"/>
        /// more reads, waiting for each to land.
        /// </summary>
        private static async Task RunReads(InventoryService sut, int refreshes, Action beforeRefresh = null)
        {
            try
            {
                await sut.StartAsync(CancellationToken.None);
                beforeRefresh?.Invoke();

                for (var i = 0; i < refreshes; i++)
                {
                    Assert.True(await sut.ForceRefreshAsync(TimeSpan.FromSeconds(10), CancellationToken.None));
                }
            }
            finally
            {
                await sut.StopAsync(CancellationToken.None);
            }
        }

        #endregion

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
        public async Task EachConnection_MessagesNameItsOwnSettings()
        {
            var options = TwoConnections();
            options.Connections[1].Inventory.Enabled = false;

            var sut = Build(options, new FakeProviderClient(ReadFrom));

            await Run(sut);

            var error = sut.GetSnapshot("lab2").VmTemplates.Error;

            Assert.Contains("Set Infrastructure:Connections:1:Inventory:Enabled to true", error);
            Assert.DoesNotContain("Set Infrastructure:Enabled", error);

            // the first connection names its own index too
            options.Connections[0].Inventory.Enabled = false;
            var first = Build(options, new FakeProviderClient(ReadFrom));
            await Run(first);
            Assert.Contains("Set Infrastructure:Connections:0:Inventory:Enabled to true", first.GetSnapshot("default").VmTemplates.Error);
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

            Assert.StartsWith("Infrastructure:Connections:1:ApiToken is not", sut.GetSnapshot("lab2").Isos.Error);
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
        public void GetConnections_ListsThemInOrderWithoutUrlsOrCredentials()
        {
            var options = TwoConnections();
            options.Connections.Add(new InfrastructureConnectionOptions { Name = "alpha", Provider = "vsphere" });

            var sut = Build(options, new FakeProviderClient(ReadFrom));

            var connections = sut.GetConnections();

            // configuration order, not alphabetical
            Assert.Equal(["default", "lab2", "alpha"], connections.Select(x => x.Name));
            Assert.True(connections[0].IsDefault);
            Assert.Equal("proxmox", connections[1].Provider);
            Assert.False(connections[2].Enabled);

            // the listing type has nowhere to put a url or a secret
            var properties = typeof(InventoryConnectionInfo).GetProperties().Select(x => x.Name).ToArray();
            Assert.Equal(["Name", "Provider", "Enabled", "IsDefault", "LastUpdated", "Resources", "Error"], properties);
        }

        [Fact]
        public void ConnectionWithAUsedName_IsIgnored()
        {
            var options = EnabledOptions();
            options.Connections.Add(new InfrastructureConnectionOptions
            {
                // names are compared ignoring case
                Name = "Default",
                Inventory = { Enabled = true },
                Provider = "proxmox",
                Url = "https://impostor.test"
            });

            var sut = Build(options, new FakeProviderClient(ReadFrom));

            var connection = Assert.Single(sut.GetConnections());
            Assert.Equal("vsphere", connection.Provider);
        }

        [Fact]
        public void ConnectionWithoutAName_IsIgnored()
        {
            var options = EnabledOptions();
            options.Connections.Add(new InfrastructureConnectionOptions { Inventory = { Enabled = true }, Provider = "proxmox", Url = "https://pve.test:8006" });

            var sut = Build(options, new FakeProviderClient(ReadFrom));

            Assert.Equal(["default"], sut.GetConnections().Select(x => x.Name));
        }

        [Fact]
        public void TheDefault_IsTheFirstEnabledConnection()
        {
            var options = TwoConnections();
            options.Connections[0].Inventory.Enabled = false;

            var sut = Build(options, new FakeProviderClient(ReadFrom));

            var connections = sut.GetConnections();
            Assert.False(connections[0].IsDefault);
            Assert.True(connections.Single(x => x.Name == "lab2").IsDefault);
        }

        [Fact]
        public void WithNoConnections_TheDefaultSaysInventoryIsNotSetUp()
        {
            var sut = Build(new InfrastructureOptions(), new FakeProviderClient(ReadFrom));

            Assert.Empty(sut.GetConnections());
            Assert.True(sut.HasConnection(null));
            Assert.False(sut.HasConnection("default"));
            Assert.Equal(InventoryMessages.NotEnabled, sut.GetSnapshot().VmTemplates.Error);
            Assert.Null(sut.GetSnapshot("default"));
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

        #region Settings

        [Fact]
        public async Task Misconfigured_IsNeverReadAndSaysWhy()
        {
            var options = TwoConnections();
            options.Connections[1].Inventory.Resources.Networks.Include = "lab-(";
            var client = new FakeProviderClient(ReadFrom);
            var sut = Build(options, client);

            await Run(sut);

            Assert.Equal(0, client.CallsTo("https://pve2.test:8006"));
            var connection = sut.GetConnections().Single(x => x.Name == "lab2");
            Assert.Contains("misconfigured", connection.Error);
            Assert.Contains("Infrastructure:Connections:1:Inventory:Resources:Networks:Include", connection.Error);

            var snapshot = sut.GetSnapshot("lab2");
            Assert.False(snapshot.Networks.Available);
            Assert.Equal(connection.Error, snapshot.VmTemplates.Error);

            // the other connection is read as usual
            Assert.Null(sut.GetConnections().Single(x => x.Name == "default").Error);
            Assert.True(sut.GetSnapshot().VmTemplates.Available);
        }

        [Fact]
        public async Task ATurnedOffKind_IsNotEnabled_AndListedAsSuch()
        {
            var options = EnabledOptions();
            options.Connections[0].Inventory.Resources.Networks.Enabled = false;
            var sut = Build(options, new FakeProviderClient(_ => Loaded()));

            await Run(sut);

            var snapshot = sut.GetSnapshot();
            Assert.False(snapshot.Networks.Enabled);
            Assert.Empty(snapshot.Networks.Entries);
            Assert.Contains("Infrastructure:Connections:0:Inventory:Resources:Networks:Enabled", snapshot.Networks.Error);
            Assert.True(snapshot.VmTemplates.Available);
            Assert.DoesNotContain(InventoryCategory.Network, sut.GetConnections()[0].Resources);
        }

        [Fact]
        public async Task Filters_AreAppliedToWhatIsCached()
        {
            var options = EnabledOptions();
            options.Connections[0].Inventory.Resources.VmTemplates.Exclude = "^win";
            var sut = Build(options, new FakeProviderClient(_ => Loaded()));

            await Run(sut);

            Assert.Empty(sut.GetSnapshot().VmTemplates.Entries);
            Assert.Single(sut.GetSnapshot().Networks.Entries);
        }

        public static TheoryData<string> VisibleChanges() =>
        [
            "Username", "Password", "Datacenters", "IsoLocations", "Resources", "Provider"
        ];

        /// <summary>
        /// Anything that changes what the connection lists empties the cache, so nothing
        /// read with the old settings is shown, or kept as stale.
        /// </summary>
        [Theory]
        [MemberData(nameof(VisibleChanges))]
        public async Task AVisibleSettingChange_DropsTheCache(string setting)
        {
            var monitor = new TestOptionsMonitor<InfrastructureOptions>(EnabledOptions());
            var reads = 0;
            var sut = Build(monitor, new FakeProviderClient(_ => Interlocked.Increment(ref reads) == 1
                ? LoadedAt(FirstRead)
                : InventorySnapshot.AllUnavailable(Refused)));

            await RunReads(sut, refreshes: 1, beforeRefresh: () =>
            {
                var changed = EnabledOptions();
                var connection = changed.Connections[0];

                switch (setting)
                {
                    case "Username": connection.Username = "someone-else"; break;
                    case "Password": connection.Password = "a-new-password"; break;
                    case "Datacenters": connection.Datacenters = ["DC2"]; break;
                    case "IsoLocations": connection.IsoLocations = ["other-ds"]; break;
                    case "Resources": connection.Inventory.Resources.Hosts.Exclude = "^esx9"; break;
                    case "Provider": connection.Provider = "proxmox"; break;
                }

                monitor.Set(changed);
                Assert.False(sut.GetSnapshot().VmTemplates.Available);
            });

            var snapshot = sut.GetSnapshot();
            Assert.False(snapshot.VmTemplates.Available);
            Assert.False(snapshot.VmTemplates.Stale);
            Assert.Empty(snapshot.VmTemplates.Entries);
        }

        [Fact]
        public async Task ARefreshIntervalChange_KeepsTheCache()
        {
            var monitor = new TestOptionsMonitor<InfrastructureOptions>(EnabledOptions());
            var reads = 0;
            var sut = Build(monitor, new FakeProviderClient(_ => Interlocked.Increment(ref reads) == 1
                ? LoadedAt(FirstRead)
                : InventorySnapshot.AllUnavailable(Refused)));

            await RunReads(sut, refreshes: 1, beforeRefresh: () =>
            {
                var changed = EnabledOptions();
                changed.Connections[0].Inventory.RefreshMinutes = 5;
                monitor.Set(changed);
            });

            // the failed refresh keeps the read from before, as stale
            var templates = sut.GetSnapshot().VmTemplates;
            Assert.True(templates.Available);
            Assert.True(templates.Stale);
        }

        /// <summary>
        /// A read that was running when the settings changed finishes with the old
        /// settings. It is discarded, never cached, and the next read uses the new ones.
        /// </summary>
        [Fact]
        public async Task AReadStartedBeforeASettingsChange_IsDiscarded()
        {
            var monitor = new TestOptionsMonitor<InfrastructureOptions>(EnabledOptions());
            var client = new BlockingProviderClient();
            var sut = Build(monitor, client);

            try
            {
                var starting = sut.StartAsync(CancellationToken.None);
                await client.FirstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

                var changed = EnabledOptions();
                changed.Connections[0].Password = "rotated-password";
                monitor.Set(changed);

                client.Release.TrySetResult();
                await starting;

                // the old read is not shown, even for a moment
                Assert.DoesNotContain(sut.GetSnapshot().VmTemplates.Entries, x => x.Name == "read-with-sup3r-s3cret-pw");

                await WaitFor(() => sut.GetSnapshot().VmTemplates.Available);
                Assert.Equal("read-with-rotated-password", Assert.Single(sut.GetSnapshot().VmTemplates.Entries).Name);
            }
            finally
            {
                client.Release.TrySetResult();
                await sut.StopAsync(CancellationToken.None);
            }
        }

        [Fact]
        public async Task ProviderThrows_LogsOnlyTheRedactedMessage()
        {
            const string tokenSecret = "lab2-test-secret-not-real";
            var options = TwoConnections();
            var logger = new Crucible.Api.Testing.RecordingLogger<InventoryService>();
            var client = new FakeProviderClient(o => o.Url == "https://pve2.test:8006"
                ? throw new InvalidOperationException($"refused {Lab2Token}, secret {tokenSecret}")
                : throw new InvalidOperationException($"login failed for {Password}"));
            var sut = new InventoryService(new TestOptionsMonitor<InfrastructureOptions>(options), client, logger);

            await Run(sut);

            var errors = logger.At(LogLevel.Error);
            Assert.Equal(2, errors.Count);
            Assert.All(errors, x =>
            {
                Assert.Null(x.Exception);
                Assert.DoesNotContain(Password, x.Message);
                Assert.DoesNotContain(Lab2Token, x.Message);
                Assert.DoesNotContain(tokenSecret, x.Message);
                Assert.Contains("InvalidOperationException", x.Message);
            });
            Assert.DoesNotContain(tokenSecret, sut.GetSnapshot("lab2").VmTemplates.Error);
        }

        /// <summary>
        /// Refreshes that overlap a read wait for it rather than queue a read each: however
        /// many arrive, at most one more read follows the one in flight.
        /// </summary>
        [Fact]
        public async Task OverlappingRefreshes_AreMerged()
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            var client = new FakeProviderClient(_ =>
            {
                if (Interlocked.Increment(ref calls) == 2)
                {
                    blocked.TrySetResult();
                    gate.Task.Wait(TimeSpan.FromSeconds(10));
                }

                return Loaded();
            });
            var sut = Build(EnabledOptions(), client);

            try
            {
                await sut.StartAsync(CancellationToken.None);
                sut.ForceRefresh();
                await blocked.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

                var refreshes = Enumerable.Range(0, 5)
                    .Select(_ => sut.ForceRefreshAsync(TimeSpan.FromSeconds(10), CancellationToken.None))
                    .ToArray();

                gate.TrySetResult();

                Assert.All(await Task.WhenAll(refreshes), Assert.True);
                await WaitFor(() => Volatile.Read(ref calls) >= 3);
                await Task.Delay(200, TestContext.Current.CancellationToken);

                Assert.Equal(3, Volatile.Read(ref calls));
            }
            finally
            {
                gate.TrySetResult();
                await sut.StopAsync(CancellationToken.None);
            }
        }

        /// <summary>The first read waits to be released; every read names the password it used.</summary>
        private sealed class BlockingProviderClient : IInventoryProviderClient
        {
            private int _calls;

            public TaskCompletionSource FirstCallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public async Task<InventorySnapshot> GetInventoryAsync(InfrastructureConnectionOptions options, CancellationToken cancellationToken)
            {
                if (Interlocked.Increment(ref _calls) == 1)
                {
                    FirstCallStarted.TrySetResult();
                    await Release.Task;
                }

                return new InventorySnapshot
                {
                    LastUpdated = DateTime.UtcNow,
                    VmTemplates = InventoryCategoryResult.Ok([
                        new InventoryEntry { Id = "vm-1", Name = $"read-with-{options.Password}", Path = "vm-1" }
                    ])
                };
            }
        }

        #endregion

        #region Helpers

        /// <summary>One enabled vSphere connection, named default.</summary>
        private static InfrastructureOptions EnabledOptions() => new()
        {
            Connections =
            [
                new InfrastructureConnectionOptions
                {
                    Name = "default",
                    Inventory = { Enabled = true, RefreshMinutes = 600 },
                    Provider = "vsphere",
                    Url = "https://vcenter.test",
                    Username = "caster-ro@vsphere.local",
                    Password = Password,
                }
            ]
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
            Isos = InventoryCategoryResult.Unavailable("ISOs could not be read.")
        };

        private static InventoryService Build(InfrastructureOptions options, IInventoryProviderClient client) =>
            Build(new TestOptionsMonitor<InfrastructureOptions>(options), client);

        private static InventoryService Build(IOptionsMonitor<InfrastructureOptions> monitor, IInventoryProviderClient client) =>
            new(monitor, client, Substitute.For<ILogger<InventoryService>>());

        // Not shaped like a real token secret (a UUID), so secret scanning leaves it alone.
        private const string Lab2Token = "caster@pve!lab2=lab2-test-secret-not-real";

        /// <summary>
        /// The default connection plus an enabled Proxmox connection named lab2,
        /// with nothing in common, so a test can tell which settings a read used.
        /// </summary>
        private static InfrastructureOptions TwoConnections()
        {
            var options = EnabledOptions();

            options.Connections.Add(new InfrastructureConnectionOptions
            {
                Name = "lab2",
                Inventory = { Enabled = true, RefreshMinutes = 600 },
                Provider = "proxmox",
                Url = "https://pve2.test:8006",
                ApiToken = Lab2Token,
            });

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
