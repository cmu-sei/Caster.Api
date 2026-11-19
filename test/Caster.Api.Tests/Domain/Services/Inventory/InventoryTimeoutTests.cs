// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Services.Inventory;
using Caster.Api.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Caster.Api.Tests.Domain.Services.Inventory
{
    /// <summary>
    /// Infrastructure:Connections:N:TimeoutSeconds: one timeout for every request a
    /// connection makes, validated, and defaulting to the 1 minute the readers always used.
    /// </summary>
    [Trait("Category", "Unit")]
    [Trait("Category", "Inventory")]
    public class InventoryTimeoutTests
    {
        private const string Password = "sup3r-s3cret-pw";

        private static InfrastructureConnectionOptions Options(string provider = "vsphere", int? timeout = null)
        {
            var options = new InfrastructureConnectionOptions
            {
                Name = "lab",
                Provider = provider,
                Url = provider == "vsphere" ? "https://vcenter.test" : "https://pve.test:8006",
                Username = "caster@pve",
                Password = Password,
                Inventory = { Enabled = true }
            };

            if (timeout is { } seconds)
                options.TimeoutSeconds = seconds;

            return options;
        }

        [Fact]
        public void TimeoutSeconds_DefaultsToTheOneMinuteTheReadersAlwaysUsed()
        {
            Assert.Equal(60, new InfrastructureConnectionOptions().TimeoutSeconds);
            Assert.Null(InventoryFilters.Validate(Options()));
            Assert.Equal(TimeSpan.FromSeconds(60), InventoryHttpClients.Timeout(Options()));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(300)]
        [InlineData(600)]
        public void TimeoutSeconds_InRangeIsAccepted(int seconds)
        {
            Assert.Null(InventoryFilters.Validate(Options(timeout: seconds)));
            Assert.Equal(TimeSpan.FromSeconds(seconds), InventoryHttpClients.Timeout(Options(timeout: seconds)));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        [InlineData(601)]
        public void TimeoutSeconds_OutOfRangeIsRejectedWithAClearMessage(int seconds)
        {
            foreach (var provider in new[] { "vsphere", "proxmox" })
            {
                var problem = InventoryFilters.Validate(Options(provider, seconds));

                Assert.Equal(
                    $"Infrastructure:TimeoutSeconds must be a whole number of seconds from 1 to 600, but is {seconds}.",
                    problem);
                Assert.DoesNotContain(Password, problem);
            }
        }

        [Fact]
        public void TimeoutSeconds_NamesTheConnectionsOwnKeyOnceScopedAndIsNeverRead()
        {
            var options = new InfrastructureOptions { Connections = [Options(timeout: 0)] };
            var monitor = Substitute.For<IOptionsMonitor<InfrastructureOptions>>();
            monitor.CurrentValue.Returns(options);
            var service = new InventoryService(
                monitor,
                Substitute.For<IInventoryProviderClient>(),
                Substitute.For<ILogger<InventoryService>>());

            var error = Assert.Single(service.GetConnections()).Error;

            Assert.Contains("Infrastructure:Connections:0:TimeoutSeconds must be a whole number", error);
        }

        [Fact]
        public void TimeoutSeconds_ReadsFromEnvironmentVariableStyleConfiguration()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Infrastructure:Connections:0:Name"] = "lab",
                    // what Infrastructure__Connections__0__TimeoutSeconds becomes
                    ["Infrastructure:Connections:0:TimeoutSeconds"] = "240"
                })
                .Build();

            var bound = configuration.GetSection("Infrastructure").Get<InfrastructureOptions>();

            Assert.Equal(240, Assert.Single(bound.Connections).TimeoutSeconds);
        }

        [Fact]
        public void TimeoutSeconds_UnusableValueFallsBackToTheDefaultWhereValidationWasSkipped()
        {
            Assert.Equal(TimeSpan.FromSeconds(60), InventoryHttpClients.Timeout(Options(timeout: 0)));
            Assert.Equal(TimeSpan.FromSeconds(60), InventoryHttpClients.Timeout(Options(timeout: 100000)));
            Assert.Equal(TimeSpan.FromSeconds(60), InventoryHttpClients.Timeout(null));
        }

        [Fact]
        public void VimClient_DefaultKeepsTheOneMinuteSendTimeout()
        {
            var client = VimInventorySource.CreateClient(Options());

            Assert.Equal(TimeSpan.FromSeconds(60), client.Endpoint.Binding.SendTimeout);
            Assert.Equal(TimeSpan.FromSeconds(60), client.Endpoint.Binding.ReceiveTimeout);
        }

        [Fact]
        public void VimClient_ConfiguredValueReachesTheBinding()
        {
            var client = VimInventorySource.CreateClient(Options(timeout: 240));

            Assert.Equal(TimeSpan.FromSeconds(240), client.Endpoint.Binding.SendTimeout);
            Assert.Equal(TimeSpan.FromSeconds(240), client.Endpoint.Binding.ReceiveTimeout);

            // not the connectionless open and close, which don't wait on vCenter's answers
            Assert.Equal(TimeSpan.FromMinutes(1), client.Endpoint.Binding.OpenTimeout);
            Assert.Equal(TimeSpan.FromMinutes(1), client.Endpoint.Binding.CloseTimeout);
        }

        [Fact]
        public async Task VsphereRestClient_UsesTheConnectionsTimeout()
        {
            var factory = new RecordingFactory();
            var source = Substitute.For<IVimInventorySource>();
            source.ReadAsync(Arg.Any<InfrastructureConnectionOptions>(), Arg.Any<CancellationToken>())
                .Returns(new VimInventory());
            var client = new VsphereInventoryClient(factory, source, Substitute.For<ILogger<VsphereInventoryClient>>());

            await client.GetInventoryAsync(Options(timeout: 240), CancellationToken.None);

            Assert.All(factory.Clients, x => Assert.Equal(TimeSpan.FromSeconds(240), x.Timeout));
            Assert.NotEmpty(factory.Clients);
        }

        [Fact]
        public async Task ProxmoxClient_UsesTheConnectionsTimeout()
        {
            var factory = new RecordingFactory();
            var client = new ProxmoxInventoryClient(factory, Substitute.For<ILogger<ProxmoxInventoryClient>>());
            var options = Options("proxmox", 240);
            options.ApiToken = "caster@pve!inventory=00000000-0000-0000-0000-000000000000";

            await client.GetInventoryAsync(options, CancellationToken.None);

            Assert.All(factory.Clients, x => Assert.Equal(TimeSpan.FromSeconds(240), x.Timeout));
            Assert.NotEmpty(factory.Clients);
        }

        [Fact]
        public async Task Clients_WithNoTimeoutConfiguredKeepTheDefault()
        {
            var factory = new RecordingFactory();
            var client = new ProxmoxInventoryClient(factory, Substitute.For<ILogger<ProxmoxInventoryClient>>());
            var options = Options("proxmox");
            options.ApiToken = "caster@pve!inventory=00000000-0000-0000-0000-000000000000";

            await client.GetInventoryAsync(options, CancellationToken.None);

            Assert.All(factory.Clients, x => Assert.Equal(TimeSpan.FromSeconds(60), x.Timeout));
            Assert.NotEmpty(factory.Clients);
        }

        /// <summary>Hands out clients on a handler that always fails, and keeps them to inspect.</summary>
        private sealed class RecordingFactory : IHttpClientFactory
        {
            public List<HttpClient> Clients { get; } = [];

            public HttpClient CreateClient(string name)
            {
                var client = new HttpClient(new FailingHandler(), disposeHandler: true);
                Clients.Add(client);
                return client;
            }
        }

        private sealed class FailingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }
}
