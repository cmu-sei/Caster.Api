// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Services.Inventory;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Caster.Api.Tests.Unit.Services
{
    /// <summary>
    /// Provider selection used to live inside the vSphere client. These cover it
    /// in its own seam, so adding a third provider is a registration and nothing
    /// else.
    /// </summary>
    [Trait("Category", "Unit")]
    public class InventoryProviderDispatcherTests
    {
        [Theory]
        [InlineData("vsphere")]
        [InlineData("VSPHERE")]
        [InlineData(" vsphere ")]
        public async Task Dispatch_SelectsVsphere(string configured)
        {
            var vsphere = new FakeProvider(InfrastructureProviders.Vsphere);
            var proxmox = new FakeProvider(InfrastructureProviders.Proxmox);

            var snapshot = await Dispatch(configured, vsphere, proxmox);

            Assert.Equal(1, vsphere.Calls);
            Assert.Equal(0, proxmox.Calls);
            Assert.True(snapshot.VmTemplates.Available);
            Assert.Equal("vsphere", Assert.Single(snapshot.VmTemplates.Entries).Name);
        }

        [Theory]
        [InlineData("proxmox")]
        [InlineData("Proxmox")]
        public async Task Dispatch_SelectsProxmox(string configured)
        {
            var vsphere = new FakeProvider(InfrastructureProviders.Vsphere);
            var proxmox = new FakeProvider(InfrastructureProviders.Proxmox);

            var snapshot = await Dispatch(configured, vsphere, proxmox);

            Assert.Equal(0, vsphere.Calls);
            Assert.Equal(1, proxmox.Calls);
            Assert.Equal("proxmox", Assert.Single(snapshot.VmTemplates.Entries).Name);
        }

        [Theory]
        [InlineData("hyperv")]
        [InlineData("")]
        [InlineData(null)]
        public async Task Dispatch_UnknownProvider_ReportsUnsupportedAndCallsNobody(string configured)
        {
            var vsphere = new FakeProvider(InfrastructureProviders.Vsphere);
            var proxmox = new FakeProvider(InfrastructureProviders.Proxmox);

            var snapshot = await Dispatch(configured, vsphere, proxmox);

            Assert.Equal(0, vsphere.Calls);
            Assert.Equal(0, proxmox.Calls);

            Assert.Null(snapshot.LastUpdated);
            Assert.False(snapshot.VmTemplates.Available);
            Assert.False(snapshot.Isos.Available);
            Assert.False(snapshot.Networks.Available);
            Assert.False(snapshot.Datastores.Available);

            Assert.All(
                new[] { snapshot.VmTemplates.Error, snapshot.Isos.Error, snapshot.Networks.Error, snapshot.Datastores.Error },
                x => Assert.Equal(InventoryMessages.UnsupportedProvider(configured), x));
        }

        [Fact]
        public void UnsupportedProvider_NamesEveryRegisteredProvider()
        {
            var message = InventoryMessages.UnsupportedProvider("hyperv");

            Assert.Contains("'hyperv' is not supported", message);

            foreach (var provider in InfrastructureProviders.All)
            {
                Assert.Contains($"'{provider}'", message);
            }
        }

        [Fact]
        public async Task Dispatch_PassesTheOptionsAndTokenStraightThrough()
        {
            var proxmox = new FakeProvider(InfrastructureProviders.Proxmox);
            using var cts = new CancellationTokenSource();

            var options = new InfrastructureOptions
            {
                Enabled = true,
                Provider = InfrastructureProviders.Proxmox,
                Url = "https://pve.test:8006"
            };

            var dispatcher = new InventoryProviderDispatcher(
                [proxmox],
                Substitute.For<ILogger<InventoryProviderDispatcher>>());

            await dispatcher.GetInventoryAsync(options, cts.Token);

            Assert.Same(options, proxmox.LastOptions);
            Assert.Equal(cts.Token, proxmox.LastToken);
        }

        [Fact]
        public async Task Dispatch_NoProvidersRegistered_ReportsUnsupported()
        {
            var dispatcher = new InventoryProviderDispatcher(
                [],
                Substitute.For<ILogger<InventoryProviderDispatcher>>());

            var snapshot = await dispatcher.GetInventoryAsync(
                new InfrastructureOptions { Provider = InfrastructureProviders.Vsphere }, CancellationToken.None);

            Assert.False(snapshot.VmTemplates.Available);
            Assert.Equal(InventoryMessages.UnsupportedProvider(InfrastructureProviders.Vsphere), snapshot.VmTemplates.Error);
        }

        /// <summary>
        /// The dispatcher is the single IInventoryProviderClient, so
        /// InventoryService keeps depending on one thing no matter how many
        /// providers exist.
        /// </summary>
        [Fact]
        public void Dispatcher_IsAnInventoryProviderClient()
        {
            Assert.True(typeof(IInventoryProviderClient).IsAssignableFrom(typeof(InventoryProviderDispatcher)));

            // and the providers are not, so they cannot be resolved by accident
            Assert.True(typeof(IInventoryProvider).IsAssignableFrom(typeof(VsphereInventoryClient)));
            Assert.True(typeof(IInventoryProvider).IsAssignableFrom(typeof(ProxmoxInventoryClient)));
            Assert.False(typeof(IInventoryProvider).IsAssignableFrom(typeof(InventoryProviderDispatcher)));
        }

        [Fact]
        public void EveryProviderHasItsOwnHttpClientName()
        {
            Assert.NotEqual(VsphereInventoryClient.HttpClientName, ProxmoxInventoryClient.HttpClientName);
        }

        /// <summary>
        /// AddInventoryServices must resolve with no Infrastructure configuration
        /// at all, and must hand InventoryService the dispatcher rather than one of
        /// the providers.
        /// </summary>
        [Fact]
        public void AddInventoryServices_ResolvesTheDispatcherAndBothProviders()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOptions<InfrastructureOptions>();
            services.AddInventoryServices();

            using var provider = services.BuildServiceProvider(validateScopes: true);

            Assert.IsType<InventoryProviderDispatcher>(provider.GetRequiredService<IInventoryProviderClient>());

            var registered = provider.GetServices<IInventoryProvider>().ToArray();

            Assert.Equal(2, registered.Length);
            Assert.Contains(registered, x => x.Provider == InfrastructureProviders.Vsphere);
            Assert.Contains(registered, x => x.Provider == InfrastructureProviders.Proxmox);

            // every registered provider name is one InfrastructureProviders knows about,
            // so the "not supported" message can never omit a real provider
            Assert.All(registered, x => Assert.Contains(x.Provider, InfrastructureProviders.All));

            // and each one has its own named http client with its own handler
            var factory = provider.GetRequiredService<IHttpClientFactory>();

            Assert.All(registered, x =>
            {
                using var client = factory.CreateClient(x.HttpClientName);
                Assert.Equal(TimeSpan.FromSeconds(60), client.Timeout);
            });
        }

        [Fact]
        public async Task AddInventoryServices_UnconfiguredProxmox_ReportsNotConfiguredNotUnsupported()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOptions<InfrastructureOptions>();
            services.AddInventoryServices();

            using var provider = services.BuildServiceProvider(validateScopes: true);

            var snapshot = await provider.GetRequiredService<IInventoryProviderClient>().GetInventoryAsync(
                new InfrastructureOptions { Enabled = true, Provider = InfrastructureProviders.Proxmox },
                CancellationToken.None);

            // proxmox was selected, and failed on its own missing credentials
            Assert.Equal(InventoryMessages.ProxmoxNotConfigured, snapshot.VmTemplates.Error);
        }

        #region Helpers

        private static async Task<InventorySnapshot> Dispatch(string configured, params IInventoryProvider[] providers)
        {
            var dispatcher = new InventoryProviderDispatcher(
                providers,
                Substitute.For<ILogger<InventoryProviderDispatcher>>());

            return await dispatcher.GetInventoryAsync(
                new InfrastructureOptions { Enabled = true, Provider = configured }, CancellationToken.None);
        }

        private sealed class FakeProvider(string provider) : IInventoryProvider
        {
            private int _calls;

            public int Calls => _calls;

            public InfrastructureOptions LastOptions { get; private set; }

            public CancellationToken LastToken { get; private set; }

            public string Provider => provider;

            public string HttpClientName => $"{provider}-inventory";

            public Task<InventorySnapshot> GetInventoryAsync(InfrastructureOptions options, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _calls);
                LastOptions = options;
                LastToken = cancellationToken;

                return Task.FromResult(new InventorySnapshot
                {
                    VmTemplates = InventoryCategoryResult.Ok([new InventoryEntry { Id = provider, Name = provider }])
                });
            }
        }

        #endregion
    }
}
