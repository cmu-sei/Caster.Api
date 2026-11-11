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
using Crucible.Api.Testing;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Caster.Api.Tests.Domain.Services.Inventory
{
    /// <summary>
    /// Exercises the vCenter reader against a fake vim25 (SOAP) source and canned
    /// Automation REST responses for content libraries. There is no vCenter
    /// available to the test suite, so these cover the mapping and the contract,
    /// not the live api.
    /// </summary>
    [Trait("Category", "Unit")]
    public class VsphereInventoryClientTests
    {
        private const string Password = "sup3r-s3cret-pw";

        private const string SessionBody = "\"tok-abc123\"";

        private static InfrastructureConnectionOptions Options() => new()
        {
            Name = "lab",
            Inventory = { Enabled = true, RefreshMinutes = 30 },
            Provider = "vsphere",
            Url = "https://vcenter.test",
            Username = "caster-ro@vsphere.local",
            Password = Password,
            InsecureSkipVerify = true,
        };

        /// <summary>
        /// DC1's VM folder group-v1 holds Templates (group-v2), which holds Linux
        /// (group-v3). win2019-template is in Templates, alpine-template in
        /// Templates/Linux, running-vm at the top, and orphan-template under a folder
        /// that was not read. Two hosts: host-1 runs running-vm and has an idle VM.
        /// host-1 is in a cluster, vlan-200 and nfs-fast in their DC1 folders; host-2
        /// and VM Network have no parent that was read.
        /// </summary>
        private static VimInventory Vim() => new()
        {
            Datacenters = [new VimDatacenter("datacenter-1", "DC1", "group-v1")],
            Folders =
            [
                new VimFolder("group-v1", "vm", "datacenter-1"),
                new VimFolder("group-v2", "Templates", "group-v1"),
                new VimFolder("group-v3", "Linux", "group-v2"),
                new VimFolder("group-h1", "host", "datacenter-1"),
                new VimFolder("group-n1", "network", "datacenter-1"),
                new VimFolder("group-s1", "datastore", "datacenter-1")
            ],
            ComputeResources = [new VimFolder("domain-c1", "Cluster", "group-h1")],
            Vms =
            [
                new VimVm("vm-1", "running-vm", false, "poweredOn", 2, 4096, "group-v1", "host-1"),
                new VimVm("vm-4", "idle-vm", false, "poweredOff", 8, 16384, "group-v1", "host-1"),
                new VimVm("vm-2", "win2019-template", true, "poweredOff", 4, 8192, "group-v2", "host-2"),
                new VimVm("vm-3", "alpine-template", true, "poweredOff", 1, 1024, "group-v3", "host-2"),
                new VimVm("vm-5", "orphan-template", true, "poweredOff", 1, 512, "group-missing", null)
            ],
            Hosts =
            [
                new VimHost("host-1", "esx1.test", "connected", "poweredOn", 1000, 2000, 4, 2048, 8L * 1024 * 1024 * 1024, "domain-c1"),
                new VimHost("host-2", "esx2.test", "disconnected", "poweredOff", null, null, null, null, null)
            ],
            Networks =
            [
                new VimNetwork("dvportgroup-1", "vlan-200", "DistributedVirtualPortgroup", false, "group-n1"),
                new VimNetwork("dvportgroup-2", "DSwitch-Uplinks", "DistributedVirtualPortgroup", true),
                new VimNetwork("network-1", "VM Network", "Network", false)
            ],
            Datastores =
            [
                new VimDatastore("datastore-1", "nfs-fast", "NFS", 4096, 1024, true, "browser-1", "group-s1")
            ],
            Isos =
            [
                new VimIso("nfs-fast", "[nfs-fast] iso/alpine.iso", "alpine.iso", 18),
                new VimIso("nfs-fast", "[nfs-fast] kali.iso", "kali.iso", null)
            ]
        };

        [Fact]
        public async Task GetInventoryAsync_ListsOnlyTemplatesWithTheirFolderPaths()
        {
            var snapshot = await Read(DefaultResponder());

            Assert.True(snapshot.VmTemplates.Available);
            Assert.Equal(
                ["/DC1/vm/Templates/Linux/alpine-template", "orphan-template", "/DC1/vm/Templates/win2019-template"],
                snapshot.VmTemplates.Entries.Select(x => x.Path));

            var template = snapshot.VmTemplates.Entries.Single(x => x.Id == "vm-2");
            Assert.Equal("win2019-template", template.Name);
            Assert.Equal("DC1", template.Properties["datacenter"]);
            Assert.Equal("Templates", template.Properties["folder"]);
            Assert.Equal("poweredOff", template.Properties["powerState"]);
            Assert.Equal("4", template.Properties["cpuCount"]);
            Assert.Equal("8192", template.Properties["memorySizeMiB"]);
            Assert.Contains("name          = \"Templates/win2019-template\"", template.Terraform[TerraformSnippets.Vsphere]);
            Assert.Contains("name = \"DC1\"", template.Terraform[TerraformSnippets.Vsphere]);
        }

        [Fact]
        public async Task GetInventoryAsync_KeepsTheBareNameWhenTheFoldersDoNotReachADatacenter()
        {
            var snapshot = await Read(DefaultResponder());

            var orphan = snapshot.VmTemplates.Entries.Single(x => x.Id == "vm-5");
            Assert.Equal("orphan-template", orphan.Path);
            Assert.False(orphan.Properties.ContainsKey("folder"));
            Assert.False(orphan.Properties.ContainsKey("datacenter"));
        }

        [Fact]
        public async Task GetInventoryAsync_StopsAtAFolderCycle()
        {
            var vim = Vim();
            vim.Folders = [new VimFolder("group-a", "a", "group-b"), new VimFolder("group-b", "b", "group-a")];
            vim.Vms = [new VimVm("vm-9", "looped", true, "poweredOff", 1, 512, "group-a", null)];

            var snapshot = await Read(DefaultResponder(), vim: vim);

            Assert.Equal("looped", snapshot.VmTemplates.Entries.Single().Path);
        }

        [Fact]
        public async Task GetInventoryAsync_ListsNetworksWithoutUplinks()
        {
            var snapshot = await Read(DefaultResponder());

            Assert.True(snapshot.Networks.Available);
            Assert.Equal(["vlan-200", "VM Network"], snapshot.Networks.Entries.Select(x => x.Name));
            Assert.Equal("DISTRIBUTED_PORTGROUP", snapshot.Networks.Entries[0].Properties["type"]);
            Assert.Equal("STANDARD_PORTGROUP", snapshot.Networks.Entries[1].Properties["type"]);
        }

        [Fact]
        public async Task GetInventoryAsync_ListsDatastores()
        {
            var snapshot = await Read(DefaultResponder());

            var datastore = Assert.Single(snapshot.Datastores.Entries);
            Assert.Equal("datastore-1", datastore.Id);
            Assert.Equal("nfs-fast", datastore.Name);
            Assert.Equal("NFS", datastore.Properties["type"]);
            Assert.Equal("4096", datastore.Properties["capacity"]);
            Assert.Equal("1024", datastore.Properties["freeSpace"]);
            Assert.Equal("true", datastore.Properties["accessible"]);
        }

        [Fact]
        public async Task GetInventoryAsync_ListsEachHostWithItsUsageAndAllocation()
        {
            var snapshot = await Read(DefaultResponder());

            Assert.True(snapshot.Hosts.Available);
            Assert.Equal(["esx1.test", "esx2.test"], snapshot.Hosts.Entries.Select(x => x.Name));

            var host = snapshot.Hosts.Entries[0];
            Assert.Equal("host-1", host.Id);
            Assert.Equal("connected", host.Properties["connectionState"]);
            Assert.Equal("1000", host.Properties["cpuUsageMhz"]);
            Assert.Equal("8000", host.Properties["cpuCapacityMhz"]);
            Assert.Equal("0.125", host.Properties["cpu"]);
            Assert.Equal("4", host.Properties["maxcpu"]);
            Assert.Equal("2048", host.Properties["memoryUsageMiB"]);
            Assert.Equal((2048L * 1024 * 1024).ToString(), host.Properties["mem"]);
            Assert.Equal((8L * 1024 * 1024 * 1024).ToString(), host.Properties["maxmem"]);

            // templates are not guests, and only powered-on VMs count as allocated
            Assert.Equal("2", host.Properties["vmsTotal"]);
            Assert.Equal("1", host.Properties["vmsRunning"]);
            Assert.Equal("2", host.Properties["allocatedCpus"]);
            Assert.Equal("4096", host.Properties["allocatedMemoryMiB"]);

            // a host that reports no usage is still listed, without it
            var down = snapshot.Hosts.Entries[1];
            Assert.Equal("disconnected", down.Properties["connectionState"]);
            Assert.False(down.Properties.ContainsKey("cpu"));
            Assert.False(down.Properties.ContainsKey("mem"));
            Assert.Equal("0", down.Properties["vmsTotal"]);
        }

        [Fact]
        public async Task GetInventoryAsync_NamesEachIsoByItsPathOnTheDatastore()
        {
            var snapshot = await Read(DefaultResponder());

            Assert.True(snapshot.Isos.Available);
            Assert.Equal(["[nfs-fast] iso/alpine.iso", "[nfs-fast] kali.iso"], snapshot.Isos.Entries.Select(x => x.Path));
            Assert.Equal(["iso/alpine.iso", "kali.iso"], snapshot.Isos.Entries.Select(x => x.Name));
            Assert.Equal("nfs-fast", snapshot.Isos.Entries[0].Properties["datastore"]);
            Assert.Equal("18", snapshot.Isos.Entries[0].Properties["size"]);
        }

        [Fact]
        public async Task GetInventoryAsync_GivesNetworksTerraformInTheirDatacenter()
        {
            var snapshot = await Read(DefaultResponder());

            var portgroup = snapshot.Networks.Entries.Single(x => x.Name == "vlan-200");
            Assert.Equal("DC1", portgroup.Properties["datacenter"]);
            Assert.Equal([TerraformSnippets.Vsphere], portgroup.Terraform.Keys);
            Assert.Contains("data \"vsphere_network\" \"vlan_200\"", portgroup.Terraform[TerraformSnippets.Vsphere]);
            Assert.Contains("name = \"DC1\"", portgroup.Terraform[TerraformSnippets.Vsphere]);

            // without a datacenter the snippet leaves it out
            var standard = snapshot.Networks.Entries.Single(x => x.Name == "VM Network");
            Assert.False(standard.Properties.ContainsKey("datacenter"));
            Assert.DoesNotContain("datacenter", standard.Terraform[TerraformSnippets.Vsphere]);
        }

        [Fact]
        public async Task GetInventoryAsync_GivesDatastoresTerraformInTheirDatacenter()
        {
            var snapshot = await Read(DefaultResponder());

            var datastore = Assert.Single(snapshot.Datastores.Entries);
            Assert.Equal("DC1", datastore.Properties["datacenter"]);
            Assert.Contains("data \"vsphere_datastore\" \"nfs_fast\"", datastore.Terraform[TerraformSnippets.Vsphere]);
            Assert.Contains("datastore_id = data.vsphere_datastore.nfs_fast.id", datastore.Terraform[TerraformSnippets.Vsphere]);
        }

        [Fact]
        public async Task GetInventoryAsync_GivesHostsTerraformThroughTheirCluster_AndNoneWithoutADatacenter()
        {
            var snapshot = await Read(DefaultResponder());

            var clustered = snapshot.Hosts.Entries.Single(x => x.Id == "host-1");
            Assert.Equal("DC1", clustered.Properties["datacenter"]);
            Assert.Contains("data \"vsphere_host\" \"esx1_test\"", clustered.Terraform[TerraformSnippets.Vsphere]);
            Assert.Contains("host_system_id   = data.vsphere_host.esx1_test.id", clustered.Terraform[TerraformSnippets.Vsphere]);

            // vsphere_host needs a datacenter
            Assert.Empty(snapshot.Hosts.Entries.Single(x => x.Id == "host-2").Terraform);
        }

        [Fact]
        public async Task GetInventoryAsync_GivesIsosTerraformOnTheirDatastore()
        {
            var snapshot = await Read(DefaultResponder());

            var iso = snapshot.Isos.Entries.Single(x => x.Name == "iso/alpine.iso");
            Assert.Equal("DC1", iso.Properties["datacenter"]);
            var snippet = iso.Terraform[TerraformSnippets.Vsphere];
            Assert.Contains("data \"vsphere_datastore\" \"iso_alpine_iso_datastore\" {\n  name          = \"nfs-fast\"\n", snippet);
            Assert.Contains("#     path         = \"iso/alpine.iso\"\n", snippet);
        }

        [Fact]
        public async Task GetInventoryAsync_AnIsoOnADatastoreNameInTwoDatacentersLeavesTheDatacenterOut()
        {
            var vim = Vim();
            vim.Datacenters.Add(new VimDatacenter("datacenter-2", "DC2", "group-v9"));
            vim.Folders.Add(new VimFolder("group-s2", "datastore", "datacenter-2"));
            vim.Datastores.Add(new VimDatastore("datastore-2", "nfs-fast", "NFS", 4096, 1024, true, "browser-2", "group-s2"));

            var snapshot = await Read(DefaultResponder(), vim: vim);

            var iso = snapshot.Isos.Entries.Single(x => x.Name == "iso/alpine.iso");
            Assert.False(iso.Properties.ContainsKey("datacenter"));
            Assert.DoesNotContain("datacenter", iso.Terraform[TerraformSnippets.Vsphere]);
        }

        [Fact]
        public async Task GetInventoryAsync_AnIncompleteIsoSearchMakesIsosUnavailable()
        {
            // A partial list would make an ISO that exists look missing.
            var vim = Vim();
            vim.IsoError = "Some ISO locations could not be searched: [slow-ds]: timed out.";

            var snapshot = await Read(DefaultResponder(), vim: vim);

            Assert.False(snapshot.Isos.Available);
            Assert.Empty(snapshot.Isos.Entries);
            Assert.Equal(vim.IsoError, snapshot.Isos.Error);
            Assert.True(snapshot.VmTemplates.Available);
        }

        [Fact]
        public async Task GetInventoryAsync_ASoapFailureStillReadsContentLibraries()
        {
            var source = Substitute.For<IVimInventorySource>();
            source.ReadAsync(Arg.Any<InfrastructureConnectionOptions>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new TimeoutException("the vim25 endpoint did not answer"));

            var snapshot = await Read(LibraryResponder(), source: source);

            Assert.NotNull(snapshot.LastUpdated);
            Assert.True(snapshot.ContentLibrary.Available);
            Assert.Equal(2, snapshot.ContentLibrary.Entries.Length);

            foreach (var result in new[] { snapshot.VmTemplates, snapshot.Networks, snapshot.Datastores, snapshot.Hosts, snapshot.Isos })
            {
                Assert.False(result.Available);
                Assert.Equal("Could not read vCenter at https://vcenter.test: the vim25 endpoint did not answer", result.Error);
            }
        }

        [Fact]
        public async Task GetInventoryAsync_NothingReachable_EverythingUnavailableAndNotUpdated()
        {
            var source = Substitute.For<IVimInventorySource>();
            source.ReadAsync(Arg.Any<InfrastructureConnectionOptions>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException("Cannot complete login due to an incorrect user name or password."));

            var snapshot = await Read(request => request.RequestUri.AbsolutePath == "/api/session"
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : DefaultResponder()(request), source: source);

            Assert.Null(snapshot.LastUpdated);
            AssertAllUnavailable(snapshot);
            Assert.Contains("incorrect user name or password", snapshot.VmTemplates.Error);
        }

        [Fact]
        public async Task GetInventoryAsync_RejectedRestSession_OnlyContentLibraryUnavailable()
        {
            var snapshot = await Read(request => request.RequestUri.AbsolutePath == "/api/session"
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : DefaultResponder()(request));

            Assert.NotNull(snapshot.LastUpdated);
            Assert.True(snapshot.VmTemplates.Available);
            Assert.False(snapshot.ContentLibrary.Available);
            Assert.True(snapshot.ContentLibrary.Applies);
            Assert.Contains("401", snapshot.ContentLibrary.Error);
        }

        [Fact]
        public async Task GetInventoryAsync_NeverLeaksThePassword()
        {
            // error paths where the provider echoes the credentials back at us
            var source = Substitute.For<IVimInventorySource>();
            source.ReadAsync(Arg.Any<InfrastructureConnectionOptions>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException($"login failed for {Password}"));

            var snapshot = await Read(_ => throw new HttpRequestException($"auth failed for {Password}"), source: source);

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
            var source = Source(Vim());

            var snapshot = await Read(DefaultResponder(), options, source);

            AssertAllUnavailable(snapshot);
            Assert.All(AllErrors(snapshot), x => Assert.Equal(InventoryMessages.NotConfigured, x));
            await source.DidNotReceiveWithAnyArgs().ReadAsync(default, TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task GetInventoryAsync_DoesNotDispatchOnTheProviderName()
        {
            // Provider selection lives in InventoryProviderDispatcher, so this
            // client must read vCenter whenever it is called and never inspect
            // Infrastructure:Provider itself.
            var options = Options();
            options.Provider = "something-else";

            var snapshot = await Read(DefaultResponder(), options);

            Assert.True(snapshot.VmTemplates.Available);
            Assert.Equal(3, snapshot.VmTemplates.Entries.Length);
        }

        [Fact]
        public async Task GetInventoryAsync_PassesTheConnectionToTheSoapSource()
        {
            var options = Options();
            var source = Source(Vim());

            await Read(DefaultResponder(), options, source);

            await source.Received(1).ReadAsync(options, Arg.Any<CancellationToken>());
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

                return LibraryResponder()(request);
            });

            Assert.True(snapshot.ContentLibrary.Available);
            Assert.Equal("tok-abc123", sessionHeader);
            Assert.Contains("POST /api/session", seen);
            Assert.Contains("DELETE /api/session", seen);

            // REST is only used for content libraries
            Assert.All(seen, x => Assert.Matches("^(POST|DELETE) /api/session$|^GET /api/content/library", x));
        }

        [Fact]
        public async Task GetInventoryAsync_HandlesLegacyValueWrapper()
        {
            var libraries = LibraryResponder();
            var snapshot = await Read(request => request.RequestUri.AbsolutePath switch
            {
                "/api/session" => Json("{ \"value\": \"tok-abc123\" }"),
                "/api/content/library" => Json("""{ "value": ["lib-1"] }"""),
                "/api/content/library/lib-1" => Json("""{ "value": { "id": "lib-1", "name": "Golden", "type": "LOCAL" } }"""),
                _ => libraries(request)
            });

            Assert.True(snapshot.ContentLibrary.Available);
            Assert.Equal(["Golden/ubuntu-2404", "Golden/win2022"], snapshot.ContentLibrary.Entries.Select(x => x.Path));
        }

        #region Content libraries

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

        #endregion

        #region Paths, datacenters and switches

        [Fact]
        public async Task GetInventoryAsync_GivesEachObjectItsFullInventoryPath()
        {
            var snapshot = await Read(DefaultResponder());

            Assert.Equal("/DC1/network/vlan-200", snapshot.Networks.Entries.Single(x => x.Name == "vlan-200").Path);
            Assert.Equal("/DC1/datastore/nfs-fast", Assert.Single(snapshot.Datastores.Entries).Path);
            Assert.Equal("/DC1/host/Cluster/esx1.test", snapshot.Hosts.Entries.Single(x => x.Id == "host-1").Path);

            // parents that weren't read leave the bare name
            Assert.Equal("esx2.test", snapshot.Hosts.Entries.Single(x => x.Id == "host-2").Path);
            Assert.Equal("VM Network", snapshot.Networks.Entries.Single(x => x.Name == "VM Network").Path);
        }

        [Fact]
        public async Task GetInventoryAsync_ADatacenterInAFolderIsInItsPath()
        {
            var vim = Vim();
            vim.Folders.Add(new VimFolder("group-d1", "Datacenters", null));
            vim.Folders.Add(new VimFolder("group-d2", "Lab", "group-d1"));
            vim.Datacenters[0] = vim.Datacenters[0] with { Parent = "group-d2" };

            var snapshot = await Read(DefaultResponder(), vim: vim);

            var template = snapshot.VmTemplates.Entries.Single(x => x.Name == "win2019-template");
            Assert.Equal("/Lab/DC1/vm/Templates/win2019-template", template.Path);
            Assert.Contains("name          = \"Templates/win2019-template\"", template.Terraform[TerraformSnippets.Vsphere]);
        }

        /// <summary>
        /// The same folder in two datacenters: a location names only one, and each
        /// snippet still finds its template by the path below its own VM folder.
        /// </summary>
        [Fact]
        public async Task GetInventoryAsync_TellsApartIdenticalFoldersInTwoDatacenters()
        {
            var vim = Vim();
            vim.Datacenters.Add(new VimDatacenter("datacenter-2", "DC2", "group-v9"));
            vim.Folders.Add(new VimFolder("group-v9", "vm", "datacenter-2"));
            vim.Folders.Add(new VimFolder("group-v8", "Templates", "group-v9"));
            vim.Vms.Add(new VimVm("vm-9", "win2019-template", true, "poweredOff", 4, 8192, "group-v8", null));

            var options = Options();
            options.Inventory.Resources.VmTemplates.Locations = ["/DC1/vm/Templates"];

            var read = await Read(DefaultResponder(), vim: vim);
            var both = read.VmTemplates.Entries.Where(x => x.Name == "win2019-template").ToArray();
            Assert.Equal(["/DC1/vm/Templates/win2019-template", "/DC2/vm/Templates/win2019-template"], both.Select(x => x.Path));
            Assert.All(both, x => Assert.Contains("name          = \"Templates/win2019-template\"", x.Terraform[TerraformSnippets.Vsphere]));
            Assert.Contains("name = \"DC2\"", both[1].Terraform[TerraformSnippets.Vsphere]);

            var filtered = InventoryFilters.Apply(read, options);

            Assert.Equal(
                ["/DC1/vm/Templates/Linux/alpine-template", "/DC1/vm/Templates/win2019-template"],
                filtered.VmTemplates.Entries.Select(x => x.Path));
        }

        /// <summary>
        /// A portgroup names its switch by reference, so two switches that share a name
        /// in different datacenters each keep their own portgroups.
        /// </summary>
        [Fact]
        public async Task GetInventoryAsync_GivesEachDistributedPortgroupItsSwitch()
        {
            var vim = Vim();
            vim.Datacenters.Add(new VimDatacenter("datacenter-2", "DC2", "group-v9"));
            vim.Folders.Add(new VimFolder("group-n2", "network", "datacenter-2"));
            vim.Switches.AddRange([new VimSwitch("dvs-1", "DSwitch"), new VimSwitch("dvs-2", "DSwitch"), new VimSwitch("dvs-3", "Lab")]);
            vim.Networks.AddRange([
                new VimNetwork("dvportgroup-11", "pg-dc1", "DistributedVirtualPortgroup", false, "group-n1", "dvs-1"),
                new VimNetwork("dvportgroup-21", "pg-dc2", "DistributedVirtualPortgroup", false, "group-n2", "dvs-2"),
                new VimNetwork("dvportgroup-31", "pg-lab", "DistributedVirtualPortgroup", false, "group-n1", "dvs-3")
            ]);

            var snapshot = await Read(DefaultResponder(), vim: vim);

            Assert.Equal("DSwitch", snapshot.Networks.Entries.Single(x => x.Name == "pg-dc1").Properties["switch"]);
            Assert.Equal("DSwitch", snapshot.Networks.Entries.Single(x => x.Name == "pg-dc2").Properties["switch"]);
            Assert.Equal("Lab", snapshot.Networks.Entries.Single(x => x.Name == "pg-lab").Properties["switch"]);
            Assert.False(snapshot.Networks.Entries.Single(x => x.Name == "VM Network").Properties.ContainsKey("switch"));

            var options = Options();
            options.Inventory.Resources.Networks.Locations = ["dswitch"];
            Assert.Equal(["pg-dc1", "pg-dc2"], InventoryFilters.Apply(snapshot, options).Networks.Entries.Select(x => x.Name));

            // a switch name and a folder together list both
            snapshot = await Read(DefaultResponder(), vim: vim);
            options.Inventory.Resources.Networks.Locations = ["Lab", "/DC2/network"];
            Assert.Equal(["pg-dc2", "pg-lab"], InventoryFilters.Apply(snapshot, options).Networks.Entries.Select(x => x.Name));
        }

        [Fact]
        public async Task GetInventoryAsync_DoesNotReadContentLibrariesThatAreTurnedOff()
        {
            var requests = new List<string>();
            var options = Options();
            options.Inventory.Resources.ContentLibraries.Enabled = false;

            var snapshot = await Read(request =>
            {
                requests.Add(request.RequestUri.AbsolutePath);
                return DefaultResponder()(request);
            }, options);

            Assert.Empty(requests);
            Assert.False(snapshot.ContentLibrary.Enabled);
            Assert.True(snapshot.VmTemplates.Available);
        }

        [Fact]
        public async Task GetInventoryAsync_ASoapFailureIsLoggedRedacted()
        {
            var source = Substitute.For<IVimInventorySource>();
            source.ReadAsync(Arg.Any<InfrastructureConnectionOptions>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException($"login failed for {Password}"));
            var logger = new RecordingLogger<VsphereInventoryClient>();

            using var handler = new StubHandler(DefaultResponder());
            var client = new VsphereInventoryClient(new StubHttpClientFactory(handler), source, logger);
            var snapshot = await client.GetInventoryAsync(Options(), CancellationToken.None);

            Assert.DoesNotContain(Password, snapshot.VmTemplates.Error);
            Assert.NotEmpty(logger.Entries);
            Assert.All(logger.Entries, x => Assert.DoesNotContain(Password, x.Message));
            Assert.Contains(logger.Entries, x => x.Message.Contains("InvalidOperationException") && x.Message.Contains("***"));
            Assert.All(logger.Entries, x => Assert.Null(x.Exception));
        }

        #endregion

        #region Helpers

        private static Func<HttpRequestMessage, HttpResponseMessage> DefaultResponder() =>
            request => request.RequestUri.AbsolutePath switch
            {
                "/api/session" => Json(SessionBody),
                "/api/content/library" => Json("[]"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };

        private static IVimInventorySource Source(VimInventory vim)
        {
            var source = Substitute.For<IVimInventorySource>();
            source.ReadAsync(Arg.Any<InfrastructureConnectionOptions>(), Arg.Any<CancellationToken>()).Returns(vim);
            return source;
        }

        private static async Task<InventorySnapshot> Read(
            Func<HttpRequestMessage, HttpResponseMessage> responder,
            InfrastructureConnectionOptions options = null,
            IVimInventorySource source = null,
            VimInventory vim = null)
        {
            using var handler = new StubHandler(responder);
            var client = new VsphereInventoryClient(
                new StubHttpClientFactory(handler),
                source ?? Source(vim ?? Vim()),
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
            snapshot.ContentLibrary.Error,
            snapshot.Hosts.Error
        ];

        private static void AssertAllUnavailable(InventorySnapshot snapshot)
        {
            Assert.False(snapshot.VmTemplates.Available);
            Assert.False(snapshot.Isos.Available);
            Assert.False(snapshot.Networks.Available);
            Assert.False(snapshot.Datastores.Available);
            Assert.False(snapshot.ContentLibrary.Available);
            Assert.False(snapshot.Hosts.Available);
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
