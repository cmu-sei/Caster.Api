// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Services.Inventory;
using Caster.Api.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Caster.Api.Tests.Unit.Services
{
    /// <summary>
    /// Exercises the vCenter reader against canned vSphere Automation REST
    /// responses. There is no vCenter available to the test suite, so these
    /// cover the contract and the parsing, not the live api.
    /// </summary>
    [Trait("Category", "Unit")]
    public class VsphereInventoryClientTests
    {
        private const string Password = "sup3r-s3cret-pw";

        private const string SessionBody = "\"tok-abc123\"";

        private const string VmBody = """
        [
          { "vm": "vm-1", "name": "running-vm", "power_state": "POWERED_ON", "cpu_count": 2, "memory_size_MiB": 4096, "template": false },
          { "vm": "vm-2", "name": "win2019-template", "power_state": "POWERED_OFF", "cpu_count": 4, "memory_size_MiB": 8192, "template": true },
          { "vm": "vm-3", "name": "alpine-template", "power_state": "POWERED_OFF", "cpu_count": 1, "memory_size_MiB": 1024, "template": true }
        ]
        """;

        private const string NetworkBody = """
        [
          { "network": "network-11", "name": "vlan-200", "type": "DISTRIBUTED_PORTGROUP" },
          { "network": "network-12", "name": "VM Network", "type": "STANDARD_PORTGROUP" }
        ]
        """;

        private const string DatastoreBody = """
        [
          { "datastore": "datastore-21", "name": "nfs-fast", "type": "NFS", "free_space": 1024, "capacity": 4096 }
        ]
        """;

        private static InfrastructureOptions Options() => new()
        {
            Enabled = true,
            Provider = "vsphere",
            Url = "https://vcenter.test",
            Username = "caster-ro@vsphere.local",
            Password = Password,
            InsecureSkipVerify = true,
            RefreshMinutes = 30
        };

        [Fact]
        public async Task GetInventoryAsync_ReturnsOnlyTemplatesForVmTemplates()
        {
            var snapshot = await Read(DefaultResponder());

            Assert.True(snapshot.VmTemplates.Available);
            Assert.Null(snapshot.VmTemplates.Error);
            Assert.Equal(2, snapshot.VmTemplates.Entries.Length);
            Assert.DoesNotContain(snapshot.VmTemplates.Entries, x => x.Name == "running-vm");

            // ordered by name
            var template = snapshot.VmTemplates.Entries.Last();
            Assert.Equal("vm-2", template.Id);
            Assert.Equal("win2019-template", template.Name);
            Assert.Equal("win2019-template", template.Path);
            Assert.Equal("POWERED_OFF", template.Properties["powerState"]);
            Assert.Equal("4", template.Properties["cpuCount"]);
            Assert.Equal("8192", template.Properties["memorySizeMiB"]);
        }

        [Fact]
        public async Task GetInventoryAsync_ParsesNetworksAndDatastores()
        {
            var snapshot = await Read(DefaultResponder());

            Assert.True(snapshot.Networks.Available);
            Assert.Equal(2, snapshot.Networks.Entries.Length);
            var network = snapshot.Networks.Entries.First(x => x.Id == "network-11");
            Assert.Equal("vlan-200", network.Name);
            Assert.Equal("DISTRIBUTED_PORTGROUP", network.Properties["type"]);

            Assert.True(snapshot.Datastores.Available);
            var datastore = Assert.Single(snapshot.Datastores.Entries);
            Assert.Equal("datastore-21", datastore.Id);
            Assert.Equal("nfs-fast", datastore.Name);
            Assert.Equal("NFS", datastore.Properties["type"]);
            Assert.Equal("1024", datastore.Properties["freeSpace"]);
            Assert.Equal("4096", datastore.Properties["capacity"]);
        }

        [Fact]
        public async Task GetInventoryAsync_IsosAreReportedUnsupported()
        {
            var snapshot = await Read(DefaultResponder());

            Assert.False(snapshot.Isos.Available);
            Assert.Empty(snapshot.Isos.Entries);
            Assert.Equal(InventoryMessages.IsoNotSupported, snapshot.Isos.Error);
        }

        [Fact]
        public async Task GetInventoryAsync_SendsSessionTokenAndClosesSession()
        {
            var seen = new List<string>();
            string sessionHeader = null;

            var snapshot = await Read(request =>
            {
                seen.Add($"{request.Method} {request.RequestUri.AbsolutePath}");

                if (request.RequestUri.AbsolutePath != "/api/session" &&
                    request.Headers.TryGetValues("vmware-api-session-id", out var values))
                {
                    sessionHeader = values.FirstOrDefault();
                }

                return DefaultResponder()(request);
            });

            Assert.True(snapshot.VmTemplates.Available);
            Assert.Equal("tok-abc123", sessionHeader);
            Assert.Contains("POST /api/session", seen);
            Assert.Contains("DELETE /api/session", seen);
        }

        [Fact]
        public async Task GetInventoryAsync_HandlesLegacyValueWrapper()
        {
            var snapshot = await Read(request => request.RequestUri.AbsolutePath switch
            {
                "/api/session" => Json("{ \"value\": \"tok-abc123\" }"),
                "/api/vcenter/vm" => Json("{ \"value\": " + VmBody + " }"),
                "/api/vcenter/network" => Json("{ \"value\": " + NetworkBody + " }"),
                "/api/vcenter/datastore" => Json("{ \"value\": " + DatastoreBody + " }"),
                _ => Json("{}")
            });

            Assert.True(snapshot.VmTemplates.Available);
            Assert.Equal(2, snapshot.VmTemplates.Entries.Length);
            Assert.Equal(2, snapshot.Networks.Entries.Length);
            Assert.Single(snapshot.Datastores.Entries);
        }

        [Fact]
        public async Task GetInventoryAsync_NoTemplateFlagAnywhere_ReportsVmTemplatesUnavailable()
        {
            // Some vCenter builds omit the template flag from the VM list. Returning
            // an empty list would read as "there are no templates", which is a lie.
            var snapshot = await Read(request => request.RequestUri.AbsolutePath switch
            {
                "/api/vcenter/vm" => Json("""[ { "vm": "vm-1", "name": "running-vm", "power_state": "POWERED_ON" } ]"""),
                _ => DefaultResponder()(request)
            });

            Assert.False(snapshot.VmTemplates.Available);
            Assert.Empty(snapshot.VmTemplates.Entries);
            Assert.Contains("template flag", snapshot.VmTemplates.Error);

            // other categories are read independently and must be unaffected
            Assert.True(snapshot.Networks.Available);
        }

        [Fact]
        public async Task GetInventoryAsync_EmptyVmList_IsAvailableAndEmpty()
        {
            var snapshot = await Read(request => request.RequestUri.AbsolutePath switch
            {
                "/api/vcenter/vm" => Json("[]"),
                _ => DefaultResponder()(request)
            });

            Assert.True(snapshot.VmTemplates.Available);
            Assert.Empty(snapshot.VmTemplates.Entries);
            Assert.Null(snapshot.VmTemplates.Error);
        }

        [Fact]
        public async Task GetInventoryAsync_CategoryFailsIndependently()
        {
            var snapshot = await Read(request => request.RequestUri.AbsolutePath switch
            {
                "/api/vcenter/datastore" => new HttpResponseMessage(HttpStatusCode.Forbidden),
                _ => DefaultResponder()(request)
            });

            Assert.True(snapshot.Networks.Available);
            Assert.True(snapshot.VmTemplates.Available);
            Assert.False(snapshot.Datastores.Available);
            Assert.Contains("403", snapshot.Datastores.Error);
        }

        [Fact]
        public async Task GetInventoryAsync_RejectedCredentials_EverythingUnavailable()
        {
            var snapshot = await Read(request => request.RequestUri.AbsolutePath == "/api/session"
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : DefaultResponder()(request));

            Assert.Null(snapshot.LastUpdated);
            AssertAllUnavailable(snapshot);
            Assert.Contains("401", snapshot.VmTemplates.Error);
        }

        [Fact]
        public async Task GetInventoryAsync_NeverLeaksThePassword()
        {
            // an error path where the provider echoes the credentials back at us
            var snapshot = await Read(_ => throw new HttpRequestException($"auth failed for {Password}"));

            AssertAllUnavailable(snapshot);

            foreach (var error in AllErrors(snapshot))
            {
                Assert.DoesNotContain(Password, error);
                Assert.Contains("***", error);
            }
        }

        [Fact]
        public async Task GetInventoryAsync_IncompleteConfiguration_ReportsNotConfigured()
        {
            var options = Options();
            options.Password = null;

            var snapshot = await Read(DefaultResponder(), options);

            AssertAllUnavailable(snapshot);
            Assert.All(AllErrors(snapshot), x => Assert.Equal(InventoryMessages.NotConfigured, x));
        }

        [Fact]
        public async Task GetInventoryAsync_DoesNotDispatchOnTheProviderName()
        {
            // Provider selection moved to InventoryProviderDispatcher, so this
            // client must read vCenter whenever it is called and never inspect
            // Infrastructure:Provider itself.
            var options = Options();
            options.Provider = "something-else";

            var snapshot = await Read(DefaultResponder(), options);

            Assert.True(snapshot.VmTemplates.Available);
            Assert.Equal(2, snapshot.VmTemplates.Entries.Length);
        }

        #region Helpers

        private static Func<HttpRequestMessage, HttpResponseMessage> DefaultResponder() =>
            request => request.RequestUri.AbsolutePath switch
            {
                "/api/session" => Json(SessionBody),
                "/api/vcenter/vm" => Json(VmBody),
                "/api/vcenter/network" => Json(NetworkBody),
                "/api/vcenter/datastore" => Json(DatastoreBody),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };

        private static async Task<InventorySnapshot> Read(
            Func<HttpRequestMessage, HttpResponseMessage> responder,
            InfrastructureOptions options = null)
        {
            using var handler = new StubHandler(responder);
            var client = new VsphereInventoryClient(
                new StubHttpClientFactory(handler),
                Substitute.For<ILogger<VsphereInventoryClient>>());

            return await client.GetInventoryAsync(options ?? Options(), CancellationToken.None);
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        private static IEnumerable<string> AllErrors(InventorySnapshot snapshot) =>
        [
            snapshot.VmTemplates.Error,
            snapshot.Isos.Error,
            snapshot.Networks.Error,
            snapshot.Datastores.Error
        ];

        private static void AssertAllUnavailable(InventorySnapshot snapshot)
        {
            Assert.False(snapshot.VmTemplates.Available);
            Assert.False(snapshot.Isos.Available);
            Assert.False(snapshot.Networks.Available);
            Assert.False(snapshot.Datastores.Available);
        }

        private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(responder(request));
        }

        private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
        {
            public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
        }

        #endregion
    }
}
