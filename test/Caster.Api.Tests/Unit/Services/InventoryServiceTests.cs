// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Services.Inventory;
using Caster.Api.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Caster.Api.Tests.Unit.Services
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

                var completed = await Task.WhenAny(secondCall.Task, Task.Delay(TimeSpan.FromSeconds(10)));
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
            new(new TestOptionsMonitor<InfrastructureOptions>(options),
                client,
                Substitute.For<ILogger<InventoryService>>());

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

        private sealed class FakeProviderClient(Func<InfrastructureOptions, InventorySnapshot> responder) : IInventoryProviderClient
        {
            private int _calls;

            public int Calls => _calls;
            public InfrastructureOptions LastOptions { get; private set; }

            /// <summary>
            /// Lets a test observe the token the provider call actually receives.
            /// </summary>
            public Action<CancellationToken> OnCall { get; set; }

            public Task<InventorySnapshot> GetInventoryAsync(InfrastructureOptions options, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _calls);
                LastOptions = options;
                OnCall?.Invoke(cancellationToken);
                return Task.FromResult(responder(options));
            }
        }

        private sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
        {
            public T CurrentValue => value;

            public T Get(string name) => value;

            public IDisposable OnChange(Action<T, string> listener) => new NoopDisposable();

            private sealed class NoopDisposable : IDisposable
            {
                public void Dispose() { }
            }
        }

        #endregion
    }
}
