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
    /// Exercises the Proxmox reader against canned /api2/json payloads. Every
    /// body below is copied from real `pvesh get` output on a live PVE 9.1.1
    /// single-node host, trimmed of the fields the reader does not read, and
    /// wrapped in the "data" envelope the http api adds.
    /// <para>
    /// The transport and auth layer is not covered against a live api - no api
    /// token secret is available to the test suite - so these cover the contract,
    /// the field mapping and the header construction, not the live login.
    /// </para>
    /// </summary>
    [Trait("Category", "Unit")]
    public class ProxmoxInventoryClientTests
    {
        private const string Password = "sup3r-s3cret-pw";
        private const string TokenSecret = "8f1c2d3e-4b5a-6789-abcd-ef0123456789";
        private const string ApiToken = "caster-ro@pve!inventory=" + TokenSecret;
        private const string Ticket = "PVE:caster-ro@pve:68E0AB01::t1ck3t+s1gnature==";

        /// <summary>
        /// GET /api2/json/cluster/resources?type=vm. Note "template" is the
        /// integer 0 or 1, and "type" is qemu or lxc.
        /// </summary>
        private const string VmBody = """
        { "data": [
          { "id": "qemu/103", "vmid": 103, "name": "puppy-test", "node": "pve", "type": "qemu", "template": 0, "status": "stopped", "maxcpu": 1, "maxmem": 536870912, "maxdisk": 0 },
          { "id": "qemu/105", "vmid": 105, "name": "alpine-linux-template", "node": "pve", "type": "qemu", "template": 1, "status": "stopped", "maxcpu": 1, "maxmem": 536870912, "maxdisk": 222298112 },
          { "id": "qemu/106", "vmid": 106, "name": "tinycore-linux-template", "node": "pve", "type": "qemu", "template": 1, "status": "stopped", "maxcpu": 1, "maxmem": 536870912, "maxdisk": 0 },
          { "id": "lxc/120", "vmid": 120, "name": "alpine-ct-template", "node": "pve2", "type": "lxc", "template": 1, "status": "stopped", "maxcpu": 2, "maxmem": 1073741824, "maxdisk": 8589934592 }
        ] }
        """;

        /// <summary>
        /// GET /api2/json/storage. "content" is a comma separated list.
        /// </summary>
        private const string StorageBody = """
        { "data": [
          { "storage": "local-lvm", "type": "lvmthin", "content": "rootdir,images", "vgname": "pve" },
          { "storage": "local", "type": "dir", "content": "vztmpl,backup,iso,snippets", "path": "/var/lib/vz" }
        ] }
        """;

        private const string NodesBody = """
        { "data": [ { "node": "pve", "status": "online", "type": "node" } ] }
        """;

        /// <summary>
        /// GET /api2/json/nodes/{node}/storage/{storage}/content?content=iso.
        /// </summary>
        private const string IsoBody = """
        { "data": [
          { "content": "iso", "format": "iso", "size": 145408, "ctime": 1780672824, "volid": "local:iso/00000000-0000-0000-0000-000000000000#1-single-unit-msel.json.iso" },
          { "content": "iso", "format": "iso", "size": 62914560, "ctime": 1780672824, "volid": "local:iso/alpine-virt-3.19.2-x86_64.iso" },
          { "content": "iso", "format": "iso", "size": 25165824, "ctime": 1780672824, "volid": "local:iso/TinyCore-current.iso" }
        ] }
        """;

        /// <summary>
        /// GET /api2/json/nodes/{node}/network. Only "bridge" is a usable network.
        /// </summary>
        private const string NetworkBody = """
        { "data": [
          { "iface": "vmbr0", "type": "bridge", "cidr": "10.0.100.2/24", "bridge_ports": "nic0", "active": 1, "address": "10.0.100.2", "gateway": "10.0.100.1" },
          { "iface": "nic0", "type": "eth", "active": 1 }
        ] }
        """;

        /// <summary>
        /// GET /api2/json/cluster/sdn/vnets, which is exactly what a host without
        /// SDN configured returns.
        /// </summary>
        private const string EmptyVnetsBody = """{ "data": [] }""";

        private const string VersionBody = """{ "data": { "release": "9.1", "repoid": "42db4a6cf33dac83", "version": "9.1.1" } }""";

        private const string TicketBody = """
        { "data": { "ticket": "PVE:caster-ro@pve:68E0AB01::t1ck3t+s1gnature==", "CSRFPreventionToken": "68E0AB01:csrf", "username": "caster-ro@pve" } }
        """;

        #region VM templates

        [Fact]
        public async Task GetInventoryAsync_KeepsOnlyTheIntegerTemplateFlag()
        {
            var snapshot = await Read(DefaultResponder());

            Assert.True(snapshot.VmTemplates.Available);
            Assert.Null(snapshot.VmTemplates.Error);

            // template: 1 on three of the four, including an lxc container template
            Assert.Equal(3, snapshot.VmTemplates.Entries.Length);
            Assert.DoesNotContain(snapshot.VmTemplates.Entries, x => x.Name == "puppy-test");

            var entry = snapshot.VmTemplates.Entries.First();
            Assert.Equal("alpine-ct-template", entry.Name);
            Assert.Equal("lxc/120", entry.Id);
            Assert.Equal("alpine-ct-template", entry.Path);
            Assert.Equal("120", entry.Properties["vmid"]);
            Assert.Equal("pve2", entry.Properties["node"]);
            Assert.Equal("lxc", entry.Properties["type"]);
            Assert.Equal("stopped", entry.Properties["status"]);
            Assert.Equal("2", entry.Properties["maxcpu"]);
            Assert.Equal("1073741824", entry.Properties["maxmem"]);
            Assert.Equal("8589934592", entry.Properties["maxdisk"]);
        }

        [Fact]
        public async Task GetInventoryAsync_TemplateFlagAbsent_IsNotATemplate()
        {
            var snapshot = await Read(Responder(vm: """
            { "data": [ { "id": "qemu/103", "vmid": 103, "name": "no-flag", "node": "pve", "type": "qemu" } ] }
            """));

            Assert.True(snapshot.VmTemplates.Available);
            Assert.Empty(snapshot.VmTemplates.Entries);
        }

        [Fact]
        public async Task GetInventoryAsync_VmTemplatesAreOrderedByName()
        {
            var snapshot = await Read(DefaultResponder());

            var names = snapshot.VmTemplates.Entries.Select(x => x.Name).ToArray();

            Assert.Equal(names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase), names);
        }

        #endregion

        #region Datastores

        [Fact]
        public async Task GetInventoryAsync_MapsDatastoresFromStorage()
        {
            var snapshot = await Read(DefaultResponder());

            Assert.True(snapshot.Datastores.Available);
            Assert.Equal(2, snapshot.Datastores.Entries.Length);

            var local = snapshot.Datastores.Entries.Single(x => x.Id == "local");
            Assert.Equal("local", local.Name);
            Assert.Equal("local", local.Path);
            Assert.Equal("dir", local.Properties["type"]);
            Assert.Equal("vztmpl,backup,iso,snippets", local.Properties["content"]);
            Assert.Equal("/var/lib/vz", local.Properties["path"]);

            // lvmthin has no path, so the key is simply absent rather than empty
            var lvm = snapshot.Datastores.Entries.Single(x => x.Id == "local-lvm");
            Assert.Equal("lvmthin", lvm.Properties["type"]);
            Assert.False(lvm.Properties.ContainsKey("path"));
        }

        #endregion

        #region ISOs

        [Fact]
        public async Task GetInventoryAsync_EnumeratesIsosWithTheVolidAsTheName()
        {
            var snapshot = await Read(DefaultResponder());

            Assert.True(snapshot.Isos.Available);
            Assert.Null(snapshot.Isos.Error);
            Assert.Equal(3, snapshot.Isos.Entries.Length);

            var iso = snapshot.Isos.Entries.Single(x => x.Name.EndsWith("alpine-virt-3.19.2-x86_64.iso"));

            // the volid is already fully qualified, so it is the id, the name and the path
            Assert.Equal("local:iso/alpine-virt-3.19.2-x86_64.iso", iso.Id);
            Assert.Equal("local:iso/alpine-virt-3.19.2-x86_64.iso", iso.Name);
            Assert.Equal("local:iso/alpine-virt-3.19.2-x86_64.iso", iso.Path);
            Assert.Equal("local", iso.Properties["storage"]);
            Assert.Equal("pve", iso.Properties["node"]);
            Assert.Equal("iso", iso.Properties["format"]);
            Assert.Equal("62914560", iso.Properties["size"]);
            // "created" is ctime (unix seconds) rendered as utc iso-8601
            Assert.Equal("2026-06-05T15:20:24.0000000Z", iso.Properties["created"]);
        }

        /// <summary>
        /// This is the gap the vSphere reader cannot close - see
        /// <see cref="InventoryMessages.IsoNotSupported"/> and CRU-2828.
        /// </summary>
        [Fact]
        public async Task GetInventoryAsync_IsosAreSupportedUnlikeVsphere()
        {
            var snapshot = await Read(DefaultResponder());

            Assert.True(snapshot.Isos.Available);
            Assert.NotEqual(InventoryMessages.IsoNotSupported, snapshot.Isos.Error);
        }

        [Fact]
        public async Task GetInventoryAsync_OnlyQueriesIsoCapableStorages()
        {
            var requested = new List<string>();

            var snapshot = await Read(Track(requested, DefaultResponder()));

            Assert.True(snapshot.Isos.Available);

            // local advertises "iso" in its content list, local-lvm does not
            Assert.Contains("/api2/json/nodes/pve/storage/local/content", requested);
            Assert.DoesNotContain(requested, x => x.Contains("storage/local-lvm/content"));
        }

        [Fact]
        public async Task GetInventoryAsync_NoIsoCapableStorage_IsAvailableAndEmpty()
        {
            var snapshot = await Read(Responder(storage: """
            { "data": [ { "storage": "local-lvm", "type": "lvmthin", "content": "rootdir,images" } ] }
            """));

            Assert.True(snapshot.Isos.Available);
            Assert.Empty(snapshot.Isos.Entries);
            Assert.Null(snapshot.Isos.Error);
        }

        [Fact]
        public async Task GetInventoryAsync_MultipleNodes_EnumeratesEveryNodeAndDedupesSharedIsos()
        {
            var requested = new List<string>();

            var snapshot = await Read(Track(requested, request => request.RequestUri.AbsolutePath switch
            {
                "/api2/json/nodes" => Json("""
                { "data": [
                  { "node": "pve", "status": "online" },
                  { "node": "pve2", "status": "online" },
                  { "node": "pve3", "status": "offline" }
                ] }
                """),
                _ => DefaultResponder()(request)
            }));

            // both online nodes are asked, the offline one is skipped rather than failing the category
            Assert.Contains("/api2/json/nodes/pve/storage/local/content", requested);
            Assert.Contains("/api2/json/nodes/pve2/storage/local/content", requested);
            Assert.DoesNotContain(requested, x => x.Contains("nodes/pve3/"));

            Assert.True(snapshot.Isos.Available);

            // shared storage returns the same volids on both nodes, deduped on volid
            Assert.Equal(3, snapshot.Isos.Entries.Length);
            Assert.Equal(3, snapshot.Isos.Entries.Select(x => x.Id).Distinct().Count());
        }

        [Fact]
        public async Task GetInventoryAsync_FailedIsoStorageRead_ReportsIncompleteRatherThanAShortList()
        {
            var snapshot = await Read(request =>
                request.RequestUri.AbsolutePath.Contains("/storage/local/content")
                    ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                    : DefaultResponder()(request));

            Assert.False(snapshot.Isos.Available);
            Assert.Empty(snapshot.Isos.Entries);
            Assert.Contains("incomplete", snapshot.Isos.Error);
            Assert.Contains("403", snapshot.Isos.Error);

            // and the other three categories are unaffected
            Assert.True(snapshot.VmTemplates.Available);
            Assert.True(snapshot.Networks.Available);
            Assert.True(snapshot.Datastores.Available);
        }

        [Fact]
        public async Task GetInventoryAsync_StorageListFails_TakesDownIsosAndDatastoresOnly()
        {
            var snapshot = await Read(request => request.RequestUri.AbsolutePath == "/api2/json/storage"
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : DefaultResponder()(request));

            Assert.False(snapshot.Datastores.Available);
            Assert.False(snapshot.Isos.Available);
            Assert.Contains("storage list", snapshot.Isos.Error);

            Assert.True(snapshot.VmTemplates.Available);
            Assert.True(snapshot.Networks.Available);
        }

        #endregion

        #region Networks

        [Fact]
        public async Task GetInventoryAsync_KeepsOnlyBridges()
        {
            var snapshot = await Read(DefaultResponder());

            Assert.True(snapshot.Networks.Available);

            var bridge = Assert.Single(snapshot.Networks.Entries);
            Assert.Equal("vmbr0", bridge.Id);
            Assert.Equal("vmbr0", bridge.Name);
            Assert.Equal("vmbr0", bridge.Path);
            Assert.Equal("bridge", bridge.Properties["type"]);
            Assert.Equal("10.0.100.2/24", bridge.Properties["cidr"]);
            Assert.Equal("nic0", bridge.Properties["bridgePorts"]);
            Assert.Equal("1", bridge.Properties["active"]);
            Assert.Equal("pve", bridge.Properties["nodes"]);
        }

        [Fact]
        public async Task GetInventoryAsync_EmptySdnVnets_DoesNotFailOrAddAnything()
        {
            // our host has no SDN configured and returns [], which must not be an error
            var snapshot = await Read(DefaultResponder());

            Assert.True(snapshot.Networks.Available);
            Assert.Null(snapshot.Networks.Error);
            Assert.Single(snapshot.Networks.Entries);
        }

        [Fact]
        public async Task GetInventoryAsync_SdnNotAvailable_StillReportsBridges()
        {
            // an older or SDN-less Proxmox 404s the vnets endpoint
            var snapshot = await Read(request => request.RequestUri.AbsolutePath == "/api2/json/cluster/sdn/vnets"
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : DefaultResponder()(request));

            Assert.True(snapshot.Networks.Available);
            Assert.Null(snapshot.Networks.Error);
            Assert.Equal("vmbr0", Assert.Single(snapshot.Networks.Entries).Name);
        }

        [Fact]
        public async Task GetInventoryAsync_SdnVnets_AreIncludedAndMergedWithTheirBridge()
        {
            var snapshot = await Read(Responder(vnets: """
            { "data": [
              { "vnet": "vmbr0", "type": "vnet", "zone": "localzone", "tag": 100 },
              { "vnet": "vnet-dmz", "type": "vnet", "zone": "evpnzone", "alias": "dmz", "tag": 200 }
            ] }
            """));

            Assert.True(snapshot.Networks.Available);
            Assert.Equal(2, snapshot.Networks.Entries.Length);

            // a vnet realised as a node bridge enriches that entry instead of duplicating it
            var merged = snapshot.Networks.Entries.Single(x => x.Name == "vmbr0");
            Assert.Equal("bridge", merged.Properties["type"]);
            Assert.Equal("localzone", merged.Properties["zone"]);
            Assert.Equal("100", merged.Properties["tag"]);

            var vnet = snapshot.Networks.Entries.Single(x => x.Name == "vnet-dmz");
            Assert.Equal("vnet-dmz", vnet.Id);
            Assert.Equal("vnet-dmz", vnet.Path);
            Assert.Equal("vnet", vnet.Properties["type"]);
            Assert.Equal("evpnzone", vnet.Properties["zone"]);
            Assert.Equal("dmz", vnet.Properties["alias"]);
            Assert.Equal("200", vnet.Properties["tag"]);
        }

        [Fact]
        public async Task GetInventoryAsync_MultipleNodes_DedupesBridgesAndListsTheirNodes()
        {
            var snapshot = await Read(request => request.RequestUri.AbsolutePath switch
            {
                "/api2/json/nodes" => Json("""
                { "data": [ { "node": "pve", "status": "online" }, { "node": "pve2", "status": "online" } ] }
                """),
                "/api2/json/nodes/pve2/network" => Json("""
                { "data": [
                  { "iface": "vmbr0", "type": "bridge", "cidr": "10.0.100.3/24", "bridge_ports": "nic0", "active": 1 },
                  { "iface": "vmbr9", "type": "bridge", "bridge_ports": "nic1", "active": 0 }
                ] }
                """),
                _ => DefaultResponder()(request)
            });

            Assert.True(snapshot.Networks.Available);
            Assert.Equal(2, snapshot.Networks.Entries.Length);

            var shared = snapshot.Networks.Entries.Single(x => x.Name == "vmbr0");
            Assert.Equal("pve,pve2", shared.Properties["nodes"]);
            // first node wins for the per-node detail
            Assert.Equal("10.0.100.2/24", shared.Properties["cidr"]);

            var single = snapshot.Networks.Entries.Single(x => x.Name == "vmbr9");
            Assert.Equal("pve2", single.Properties["nodes"]);
            Assert.False(single.Properties.ContainsKey("cidr"));
        }

        [Fact]
        public async Task GetInventoryAsync_NodeListFails_TakesDownIsosAndNetworksOnly()
        {
            var snapshot = await Read(request => request.RequestUri.AbsolutePath == "/api2/json/nodes"
                ? new HttpResponseMessage(HttpStatusCode.BadGateway)
                : DefaultResponder()(request));

            Assert.False(snapshot.Networks.Available);
            Assert.Contains("node list", snapshot.Networks.Error);
            Assert.False(snapshot.Isos.Available);
            Assert.Contains("node list", snapshot.Isos.Error);

            Assert.True(snapshot.VmTemplates.Available);
            Assert.True(snapshot.Datastores.Available);
        }

        #endregion

        #region Independence

        [Fact]
        public async Task GetInventoryAsync_VmTemplatesFailIndependently()
        {
            var snapshot = await Read(request => request.RequestUri.AbsolutePath == "/api2/json/cluster/resources"
                ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                : DefaultResponder()(request));

            Assert.False(snapshot.VmTemplates.Available);
            Assert.Contains("403", snapshot.VmTemplates.Error);

            Assert.True(snapshot.Isos.Available);
            Assert.True(snapshot.Networks.Available);
            Assert.True(snapshot.Datastores.Available);
            Assert.NotNull(snapshot.LastUpdated);
        }

        #endregion

        #region Authentication

        [Fact]
        public async Task GetInventoryAsync_ApiToken_SendsThePveApiTokenHeaderAndNeverLogsIn()
        {
            var authorizations = new List<string>();
            var paths = new List<string>();

            var snapshot = await Read(request =>
            {
                paths.Add($"{request.Method} {request.RequestUri.AbsolutePath}");

                if (request.Headers.TryGetValues("Authorization", out var values))
                {
                    authorizations.Add(values.First());
                }

                return DefaultResponder()(request);
            });

            Assert.True(snapshot.VmTemplates.Available);

            // the exact wire format: PVEAPIToken=user@realm!tokenid=secret
            Assert.NotEmpty(authorizations);
            Assert.All(authorizations, x => Assert.Equal($"PVEAPIToken={ApiToken}", x));

            // a token is not exchanged for anything, so there is no ticket request
            Assert.DoesNotContain(paths, x => x.Contains("/access/ticket"));
            Assert.Contains("GET /api2/json/version", paths);
        }

        [Fact]
        public async Task GetInventoryAsync_NoApiToken_FallsBackToTheUsernamePasswordTicket()
        {
            var options = TicketOptions();
            var paths = new List<string>();
            string cookie = null;
            string csrf = null;
            var sawAuthorization = false;

            var snapshot = await Read(request =>
            {
                paths.Add($"{request.Method} {request.RequestUri.AbsolutePath}");

                sawAuthorization |= request.Headers.Contains("Authorization");

                if (request.Headers.TryGetValues("Cookie", out var cookies))
                {
                    cookie = cookies.First();
                }

                if (request.Headers.TryGetValues("CSRFPreventionToken", out var tokens))
                {
                    csrf = tokens.First();
                }

                return DefaultResponder()(request);
            }, options);

            Assert.True(snapshot.VmTemplates.Available);

            Assert.Contains("POST /api2/json/access/ticket", paths);
            Assert.False(sawAuthorization);

            // the ticket goes in the cookie url encoded, since it contains + : and =
            Assert.Equal($"PVEAuthCookie={Uri.EscapeDataString(Ticket)}", cookie);
            Assert.Equal("68E0AB01:csrf", csrf);
        }

        [Fact]
        public async Task GetInventoryAsync_PrefersTheApiTokenWhenBothAreSet()
        {
            var options = Options();
            options.Username = "caster-ro@pve";
            options.Password = Password;

            var paths = new List<string>();

            var snapshot = await Read(request =>
            {
                paths.Add($"{request.Method} {request.RequestUri.AbsolutePath}");
                return DefaultResponder()(request);
            }, options);

            Assert.True(snapshot.VmTemplates.Available);
            Assert.DoesNotContain(paths, x => x.Contains("/access/ticket"));
        }

        [Fact]
        public async Task GetInventoryAsync_RejectedApiToken_EverythingUnavailable()
        {
            var snapshot = await Read(request => request.RequestUri.AbsolutePath == "/api2/json/version"
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : DefaultResponder()(request));

            Assert.Null(snapshot.LastUpdated);
            AssertAllUnavailable(snapshot);
            Assert.Contains("401", snapshot.VmTemplates.Error);
            Assert.All(AllErrors(snapshot), x => Assert.Equal(snapshot.VmTemplates.Error, x));
        }

        [Fact]
        public async Task GetInventoryAsync_RejectedTicketLogin_EverythingUnavailable()
        {
            var snapshot = await Read(
                request => request.RequestUri.AbsolutePath == "/api2/json/access/ticket"
                    ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    : DefaultResponder()(request),
                TicketOptions());

            Assert.Null(snapshot.LastUpdated);
            AssertAllUnavailable(snapshot);
            Assert.Contains("401", snapshot.VmTemplates.Error);
        }

        [Fact]
        public async Task GetInventoryAsync_AuthenticatedButUnprivileged_SaysSo()
        {
            var snapshot = await Read(request => request.RequestUri.AbsolutePath == "/api2/json/version"
                ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                : DefaultResponder()(request));

            AssertAllUnavailable(snapshot);
            Assert.Contains("403", snapshot.VmTemplates.Error);
            Assert.Contains("lack permission", snapshot.VmTemplates.Error);
        }

        [Theory]
        [InlineData("not-a-token")]
        [InlineData("caster-ro@pve")]
        [InlineData("caster-ro@pve!inventory")]
        [InlineData("caster-ro@pve!inventory=")]
        [InlineData("caster-ro!inventory=secret")]
        public async Task GetInventoryAsync_MalformedApiToken_IsAConfigurationError(string apiToken)
        {
            var options = Options();
            options.ApiToken = apiToken;

            var calls = 0;

            var snapshot = await Read(request =>
            {
                calls++;
                return DefaultResponder()(request);
            }, options);

            AssertAllUnavailable(snapshot);
            Assert.Equal(0, calls);
            Assert.All(AllErrors(snapshot), x => Assert.Equal(InventoryMessages.ProxmoxApiTokenMalformed, x));

            // the message describes the shape and never echoes the value
            Assert.DoesNotContain(apiToken, snapshot.VmTemplates.Error);
        }

        [Fact]
        public async Task GetInventoryAsync_NoCredentialAtAll_ReportsNotConfigured()
        {
            var options = Options();
            options.ApiToken = null;

            var snapshot = await Read(DefaultResponder(), options);

            AssertAllUnavailable(snapshot);
            Assert.All(AllErrors(snapshot), x => Assert.Equal(InventoryMessages.ProxmoxNotConfigured, x));
        }

        [Fact]
        public async Task GetInventoryAsync_NoUrl_ReportsNotConfigured()
        {
            var options = Options();
            options.Url = "";

            var snapshot = await Read(DefaultResponder(), options);

            AssertAllUnavailable(snapshot);
            Assert.All(AllErrors(snapshot), x => Assert.Equal(InventoryMessages.ProxmoxNotConfigured, x));
        }

        [Theory]
        [InlineData("https://pve.test:8006", "https://pve.test:8006/api2/json/")]
        [InlineData("https://pve.test:8006/", "https://pve.test:8006/api2/json/")]
        [InlineData("https://pve.test:8006/api2/json", "https://pve.test:8006/api2/json/")]
        [InlineData("https://pve.test:8006/api2/json/", "https://pve.test:8006/api2/json/")]
        // the port is never guessed, so a reverse proxied Proxmox on 443 works
        [InlineData("https://pve.example.com", "https://pve.example.com/api2/json/")]
        public void BuildBaseUrl_AppendsTheApiRootWithoutGuessingThePort(string url, string expected)
        {
            Assert.Equal(expected, ProxmoxInventoryClient.BuildBaseUrl(url));
        }

        #endregion

        #region Secrets

        [Fact]
        public async Task GetInventoryAsync_NeverLeaksTheApiTokenOrItsSecret()
        {
            // an error path where the provider echoes the credential back at us
            var snapshot = await Read(_ => throw new HttpRequestException($"auth failed for {ApiToken}"));

            AssertAllUnavailable(snapshot);

            foreach (var error in AllErrors(snapshot))
            {
                Assert.DoesNotContain(ApiToken, error);
                Assert.DoesNotContain(TokenSecret, error);
                Assert.Contains("***", error);
            }
        }

        [Fact]
        public async Task GetInventoryAsync_NeverLeaksTheSecretHalfOnItsOwn()
        {
            var snapshot = await Read(_ => throw new HttpRequestException($"bad secret {TokenSecret}"));

            AssertAllUnavailable(snapshot);
            Assert.DoesNotContain(TokenSecret, snapshot.VmTemplates.Error);
        }

        [Fact]
        public async Task GetInventoryAsync_NeverLeaksThePassword()
        {
            var snapshot = await Read(
                _ => throw new HttpRequestException($"auth failed for {Password}"),
                TicketOptions());

            AssertAllUnavailable(snapshot);

            foreach (var error in AllErrors(snapshot))
            {
                Assert.DoesNotContain(Password, error);
                Assert.Contains("***", error);
            }
        }

        [Fact]
        public async Task GetInventoryAsync_NeverLeaksTheTicketItJustObtained()
        {
            var options = TicketOptions();

            // login succeeds, then a category read echoes the ticket back in an error
            var snapshot = await Read(request => request.RequestUri.AbsolutePath switch
            {
                "/api2/json/access/ticket" => Json(TicketBody),
                "/api2/json/version" => Json(VersionBody),
                "/api2/json/cluster/resources" => throw new HttpRequestException($"invalid cookie {Ticket}"),
                _ => DefaultResponder()(request)
            }, options);

            Assert.False(snapshot.VmTemplates.Available);
            Assert.DoesNotContain(Ticket, snapshot.VmTemplates.Error);
            Assert.Contains("***", snapshot.VmTemplates.Error);
        }

        [Fact]
        public void Redaction_IsSharedWithTheVsphereReader()
        {
            var options = new InfrastructureOptions { Password = Password, ApiToken = ApiToken };

            var redacted = InventoryRedaction.Redact($"{Password} and {ApiToken} and {TokenSecret}", options, Ticket);

            Assert.DoesNotContain(Password, redacted);
            Assert.DoesNotContain(ApiToken, redacted);
            Assert.DoesNotContain(TokenSecret, redacted);
        }

        #endregion

        #region Read only

        [Fact]
        public async Task GetInventoryAsync_OnlyIssuesGetsPlusTheOneLoginPost()
        {
            var methods = new List<string>();

            await Read(request =>
            {
                methods.Add($"{request.Method} {request.RequestUri.AbsolutePath}");
                return DefaultResponder()(request);
            }, TicketOptions());

            Assert.All(methods, x => Assert.True(
                x.StartsWith("GET ") || x == "POST /api2/json/access/ticket",
                $"unexpected non read-only request: {x}"));
        }

        #endregion

        #region Helpers

        private static InfrastructureOptions Options() => new()
        {
            Enabled = true,
            Provider = "proxmox",
            Url = "https://pve.test:8006",
            ApiToken = ApiToken,
            InsecureSkipVerify = true,
            RefreshMinutes = 30
        };

        private static InfrastructureOptions TicketOptions()
        {
            var options = Options();
            options.ApiToken = null;
            options.Username = "caster-ro@pve";
            options.Password = Password;
            return options;
        }

        private static Func<HttpRequestMessage, HttpResponseMessage> DefaultResponder() => Responder();

        private static Func<HttpRequestMessage, HttpResponseMessage> Responder(
            string vm = null,
            string storage = null,
            string nodes = null,
            string network = null,
            string isos = null,
            string vnets = null) =>
            request =>
            {
                var path = request.RequestUri.AbsolutePath;

                if (path.Contains("/storage/") && path.EndsWith("/content"))
                {
                    return Json(isos ?? IsoBody);
                }

                if (path.EndsWith("/network"))
                {
                    return Json(network ?? NetworkBody);
                }

                return path switch
                {
                    "/api2/json/access/ticket" => Json(TicketBody),
                    "/api2/json/version" => Json(VersionBody),
                    "/api2/json/cluster/resources" => Json(vm ?? VmBody),
                    "/api2/json/storage" => Json(storage ?? StorageBody),
                    "/api2/json/nodes" => Json(nodes ?? NodesBody),
                    "/api2/json/cluster/sdn/vnets" => Json(vnets ?? EmptyVnetsBody),
                    _ => new HttpResponseMessage(HttpStatusCode.NotFound)
                };
            };

        private static Func<HttpRequestMessage, HttpResponseMessage> Track(
            List<string> requested, Func<HttpRequestMessage, HttpResponseMessage> inner) =>
            request =>
            {
                requested.Add(request.RequestUri.AbsolutePath);
                return inner(request);
            };

        private static async Task<InventorySnapshot> Read(
            Func<HttpRequestMessage, HttpResponseMessage> responder,
            InfrastructureOptions options = null)
        {
            using var handler = new StubHandler(responder);
            var client = new ProxmoxInventoryClient(
                new StubHttpClientFactory(handler),
                Substitute.For<ILogger<ProxmoxInventoryClient>>());

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
