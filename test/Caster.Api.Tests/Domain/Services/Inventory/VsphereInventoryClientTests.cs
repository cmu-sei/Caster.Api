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

namespace Caster.Api.Tests.Domain.Services.Inventory
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

        #region Folders and content libraries

        /// <summary>
        /// DC1's VM folder holds Templates, which holds Linux. win2019-template sits in
        /// Templates, alpine-template in Templates/Linux and running-vm at the top. With
        /// <paramref name="subtree"/> the folders filter matches everything below a folder,
        /// otherwise only its direct children. The api docs leave it open, so both are covered.
        /// </summary>
        private static Func<HttpRequestMessage, HttpResponseMessage> FolderResponder(bool subtree) => request =>
        {
            var query = request.RequestUri.Query;

            return request.RequestUri.AbsolutePath switch
            {
                "/api/vcenter/datacenter" => Json("""[{ "datacenter": "datacenter-1", "name": "DC1" }]"""),
                "/api/vcenter/datacenter/datacenter-1" => Json("""{ "name": "DC1", "vm_folder": "group-v1" }"""),
                "/api/vcenter/folder" when query.EndsWith("parent_folders=group-v1") =>
                    Json("""[{ "folder": "group-v10", "name": "Templates", "type": "VIRTUAL_MACHINE" }]"""),
                "/api/vcenter/folder" when query.EndsWith("parent_folders=group-v10") =>
                    Json("""[{ "folder": "group-v11", "name": "Linux", "type": "VIRTUAL_MACHINE" }]"""),
                "/api/vcenter/folder" => Json("[]"),
                "/api/vcenter/vm" when query == "?folders=group-v11" => Json(Vms("vm-3")),
                "/api/vcenter/vm" when query == "?folders=group-v10" => Json(subtree ? Vms("vm-2", "vm-3") : Vms("vm-2")),
                "/api/vcenter/vm" when query == "?folders=group-v1" => Json(subtree ? Vms("vm-1", "vm-2", "vm-3") : Vms("vm-1")),
                _ => DefaultResponder()(request)
            };
        };

        private static string Vms(params string[] ids) =>
            "[" + string.Join(",", ids.Select(x => $$"""{ "vm": "{{x}}", "name": "{{x}}" }""")) + "]";

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GetInventoryAsync_GivesEachTemplateItsFolderPath(bool subtree)
        {
            var snapshot = await Read(FolderResponder(subtree));

            Assert.True(snapshot.VmTemplates.Available);
            var alpine = snapshot.VmTemplates.Entries.Single(x => x.Id == "vm-3");
            Assert.Equal("alpine-template", alpine.Name);
            Assert.Equal("Templates/Linux/alpine-template", alpine.Path);
            Assert.Equal("Templates/Linux", alpine.Properties["folder"]);
            Assert.Equal("DC1", alpine.Properties["datacenter"]);

            var windows = snapshot.VmTemplates.Entries.Single(x => x.Id == "vm-2");
            Assert.Equal("Templates/win2019-template", windows.Path);

            // the Terraform to copy finds the template by that path in that datacenter
            var terraform = Assert.Single(alpine.Terraform);
            Assert.Equal(TerraformSnippets.Vsphere, terraform.Key);
            Assert.Contains("name          = \"Templates/Linux/alpine-template\"", terraform.Value);
            Assert.Contains("name = \"DC1\"", terraform.Value);
        }

        [Fact]
        public async Task GetInventoryAsync_KeepsBareTemplateNamesWhenTheFoldersCannotBeRead()
        {
            var snapshot = await Read(request => request.RequestUri.AbsolutePath == "/api/vcenter/datacenter"
                ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                : DefaultResponder()(request));

            Assert.True(snapshot.VmTemplates.Available);
            Assert.All(snapshot.VmTemplates.Entries, x => Assert.Equal(x.Name, x.Path));
            Assert.All(snapshot.VmTemplates.Entries, x => Assert.False(x.Properties.ContainsKey("folder")));
            Assert.All(snapshot.VmTemplates.Entries, x => Assert.Contains($"name = \"{x.Name}\"", x.Terraform[TerraformSnippets.Vsphere]));
        }

        private static Func<HttpRequestMessage, HttpResponseMessage> LibraryResponder() => request =>
            request.RequestUri.AbsolutePath switch
            {
                "/api/content/library" => Json("""["lib-1"]"""),
                "/api/content/library/lib-1" => Json("""{ "id": "lib-1", "name": "Golden", "type": "LOCAL" }"""),
                "/api/content/library/item" when request.RequestUri.Query == "?library_id=lib-1" =>
                    Json("""["item-1", "item-2", "item-3", "item-gone"]"""),
                "/api/content/library/item/item-1" => Json("""{ "id": "item-1", "name": "win2022", "type": "ovf", "size": 2048, "library_id": "lib-1" }"""),
                "/api/content/library/item/item-2" => Json("""{ "id": "item-2", "name": "ubuntu-2404", "type": "vm-template", "library_id": "lib-1" }"""),
                "/api/content/library/item/item-3" => Json("""{ "id": "item-3", "name": "kali.iso", "type": "iso", "library_id": "lib-1" }"""),
                _ => DefaultResponder()(request)
            };

        [Fact]
        public async Task GetInventoryAsync_ListsContentLibraryTemplates()
        {
            var snapshot = await Read(LibraryResponder());

            Assert.True(snapshot.ContentLibrary.Available);
            Assert.True(snapshot.ContentLibrary.Applies);
            Assert.Null(snapshot.ContentLibrary.Error);

            // the ISO is left out, and the item deleted mid-read is skipped
            Assert.Equal(["Golden/ubuntu-2404", "Golden/win2022"], snapshot.ContentLibrary.Entries.Select(x => x.Path));

            var ovf = snapshot.ContentLibrary.Entries.Single(x => x.Id == "item-1");
            Assert.Equal("win2022", ovf.Name);
            Assert.Equal("Golden", ovf.Properties["library"]);
            Assert.Equal("LOCAL", ovf.Properties["libraryType"]);
            Assert.Equal("ovf", ovf.Properties["type"]);
            Assert.Equal("2048", ovf.Properties["size"]);
            Assert.Contains("type       = \"ovf\"", ovf.Terraform[TerraformSnippets.Vsphere]);
            Assert.Contains("name = \"Golden\"", ovf.Terraform[TerraformSnippets.Vsphere]);
        }

        [Fact]
        public async Task GetInventoryAsync_SkipsALibraryDeletedMidRead()
        {
            var libraries = LibraryResponder();
            var snapshot = await Read(request => request.RequestUri.AbsolutePath switch
            {
                "/api/content/library" => Json("""["lib-gone", "lib-1"]"""),
                "/api/content/library/lib-gone" => new HttpResponseMessage(HttpStatusCode.NotFound),
                _ => libraries(request)
            });

            Assert.True(snapshot.ContentLibrary.Available);
            Assert.Equal(["Golden/ubuntu-2404", "Golden/win2022"], snapshot.ContentLibrary.Entries.Select(x => x.Path));
        }

        [Fact]
        public async Task GetInventoryAsync_ContentLibraryFailureLeavesOtherCategoriesAlone()
        {
            var snapshot = await Read(request => request.RequestUri.AbsolutePath == "/api/content/library"
                ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                : DefaultResponder()(request));

            Assert.False(snapshot.ContentLibrary.Available);
            Assert.True(snapshot.ContentLibrary.Applies);
            Assert.Equal("vCenter returned 403 Forbidden when reading content libraries.", snapshot.ContentLibrary.Error);
            Assert.True(snapshot.VmTemplates.Available);
            Assert.True(snapshot.Networks.Available);
        }

        [Fact]
        public async Task GetInventoryAsync_EmptyContentLibraryIsAvailable()
        {
            var snapshot = await Read(request => request.RequestUri.AbsolutePath == "/api/content/library"
                ? Json("[]")
                : DefaultResponder()(request));

            Assert.True(snapshot.ContentLibrary.Available);
            Assert.Empty(snapshot.ContentLibrary.Entries);
        }

        [Fact]
        public async Task GetInventoryAsync_FolderWalkReadsLegacyWrappedObjects()
        {
            var folders = FolderResponder(subtree: false);
            var snapshot = await Read(request => request.RequestUri.AbsolutePath == "/api/vcenter/datacenter/datacenter-1"
                ? Json("""{ "value": { "name": "DC1", "vm_folder": "group-v1" } }""")
                : folders(request));

            Assert.Equal("Templates/Linux/alpine-template", snapshot.VmTemplates.Entries.Single(x => x.Id == "vm-3").Path);
        }

        #endregion

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
            snapshot.Datastores.Error,
            snapshot.ContentLibrary.Error
        ];

        private static void AssertAllUnavailable(InventorySnapshot snapshot)
        {
            Assert.False(snapshot.VmTemplates.Available);
            Assert.False(snapshot.Isos.Available);
            Assert.False(snapshot.Networks.Available);
            Assert.False(snapshot.Datastores.Available);
            Assert.False(snapshot.ContentLibrary.Available);
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
