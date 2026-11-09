// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using ConnectionView = Caster.Api.Features.EnvironmentSets.EnvironmentSetConnection;
using SetView = Caster.Api.Features.EnvironmentSets.EnvironmentSet;

namespace Caster.Api.Tests.Features.EnvironmentSets;

/// <summary>
/// The admin tools over several entries at once: copying a set, editing several sets together, and
/// making a set from a connection's provider preset.
/// </summary>
public class EnvironmentSetAdminRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    private const string Sets = "api/environment-sets";

    // ---- copy ----------------------------------------------------------------------------------

    [Fact]
    public async Task Copy_makes_an_unassigned_set_with_every_entry_and_a_free_name()
    {
        using var secret = new SecretEnvironmentVariable("s3cr3t-token-value");
        var set = TestData.EnvironmentSet("Lab");
        var project = TestData.Project();
        await Seed(
            set, project, TestData.EnvironmentSet("Lab (copy)"),
            TestData.PlainEntry(set, "REGION", "east"),
            TestData.SecretEntry(set, "TOKEN", secret.Name),
            TestData.ProjectEnvironmentSet(project, set));

        var response = await RootClient.PostAsJsonAsync($"{Sets}/{set.Id}/copy", new { }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        var copy = await ReadAsync<SetView>(response);
        Assert.Equal("Lab (copy) 2", copy.Name);
        Assert.Empty(copy.Projects);
        Assert.Equal(["REGION", "TOKEN"], copy.Entries.Select(x => x.TargetName).Order());
        Assert.Equal(secret.Name, copy.Entries.Single(x => x.TargetName == "TOKEN").SecretName);
        Assert.DoesNotContain(secret.Value, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Copy_of_a_set_with_connection_entries_needs_ManageInfrastructure()
    {
        var set = TestData.EnvironmentSet();
        await Seed(set, TestData.ConnectionEntry(set, "PROXMOX_VE_ENDPOINT", TestConfiguration.ProxmoxConnection, ConnectionField.Url));
        var manager = await Actor().WithSystemPermissions(SystemPermission.ManageEnvironmentSets).SeedAsync();
        var both = await Actor().WithSystemPermissions(SystemPermission.ManageEnvironmentSets, SystemPermission.ManageInfrastructure).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(manager).PostAsJsonAsync($"{Sets}/{set.Id}/copy", new { name = "copied" }, Ct));
        await AssertStatus(HttpStatusCode.OK, await Client(both).PostAsJsonAsync($"{Sets}/{set.Id}/copy", new { name = "copied" }, Ct));
    }

    [Fact]
    public async Task Copy_refuses_a_set_holding_a_secret_Terraform_variable()
    {
        using var secret = new SecretEnvironmentVariable("s3cr3t-token-value");
        var set = TestData.EnvironmentSet();
        await Seed(set, TestData.SecretEntry(set, "vsphere_password", secret.Name, EnvironmentSetEntryKind.TerraformVariable));

        await AssertProblem(HttpStatusCode.Conflict, await RootClient.PostAsJsonAsync($"{Sets}/{set.Id}/copy", new { }, Ct));
    }

    // ---- batch edit ----------------------------------------------------------------------------

    [Fact]
    public async Task BatchEdit_updates_the_sets_that_have_an_entry_upserts_into_all_and_deletes_from_all()
    {
        var a = TestData.EnvironmentSet("A");
        var b = TestData.EnvironmentSet("B");
        await Seed(
            a, b,
            TestData.PlainEntry(a, "REGION", "east"),
            TestData.PlainEntry(a, "OLD", "x"),
            TestData.PlainEntry(b, "OLD", "y"));

        var response = await RootClient.PostAsJsonAsync($"{Sets}/batch-edit", new
        {
            environmentSetIds = new[] { a.Id, b.Id },
            changes = new object[]
            {
                new { operation = "Update", kind = "EnvironmentVariable", targetName = "REGION", valueSource = "Plain", value = "west" },
                new { operation = "Upsert", kind = "TerraformVariable", targetName = "size", valueSource = "Plain", value = "\"small\"" },
                new { operation = "Delete", kind = "EnvironmentVariable", targetName = "OLD" }
            }
        }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        var stored = await ReadBack(db => db.EnvironmentSetEntries.Where(x => x.EnvironmentSetId == a.Id || x.EnvironmentSetId == b.Id).ToListAsync(Ct));
        Assert.Equal("west", stored.Single(x => x.EnvironmentSetId == a.Id && x.TargetName == "REGION").Value);
        Assert.DoesNotContain(stored, x => x.EnvironmentSetId == b.Id && x.TargetName == "REGION");
        Assert.Equal(2, stored.Count(x => x.TargetName == "size"));
        Assert.DoesNotContain(stored, x => x.TargetName == "OLD");
    }

    /// <summary>Checked against the whole final state: each set alone would pass.</summary>
    [Fact]
    public async Task BatchEdit_refuses_two_sets_on_one_project_gaining_the_same_name_and_changes_nothing()
    {
        var a = TestData.EnvironmentSet("A");
        var b = TestData.EnvironmentSet("B");
        var project = TestData.Project();
        await Seed(a, b, project, TestData.PlainEntry(a, "KEEP", "1"), TestData.ProjectEnvironmentSet(project, a), TestData.ProjectEnvironmentSet(project, b));

        var response = await RootClient.PostAsJsonAsync($"{Sets}/batch-edit", new
        {
            environmentSetIds = new[] { a.Id, b.Id },
            changes = new object[]
            {
                new { operation = "Upsert", kind = "EnvironmentVariable", targetName = "REGION", valueSource = "Plain", value = "east" },
                new { operation = "Delete", kind = "EnvironmentVariable", targetName = "KEEP" }
            }
        }, Ct);

        await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.Contains("REGION", await response.Content.ReadAsStringAsync(Ct));
        var stored = await ReadBack(db => db.EnvironmentSetEntries.Where(x => x.EnvironmentSetId == a.Id || x.EnvironmentSetId == b.Id).ToListAsync(Ct));
        Assert.Equal(["KEEP"], stored.Select(x => x.TargetName));
    }

    [Fact]
    public async Task BatchEdit_that_changes_a_connection_entry_needs_ManageInfrastructure_but_removing_one_does_not()
    {
        var a = TestData.EnvironmentSet();
        await Seed(a, TestData.ConnectionEntry(a, "PROXMOX_VE_ENDPOINT", TestConfiguration.ProxmoxConnection, ConnectionField.Url));
        var manager = await Actor().WithSystemPermissions(SystemPermission.ManageEnvironmentSets).SeedAsync();

        var change = await Client(manager).PostAsJsonAsync($"{Sets}/batch-edit", new
        {
            environmentSetIds = new[] { a.Id },
            changes = new object[] { new { operation = "Update", kind = "EnvironmentVariable", targetName = "PROXMOX_VE_ENDPOINT", valueSource = "Plain", value = "https://elsewhere" } }
        }, Ct);
        var remove = await Client(manager).PostAsJsonAsync($"{Sets}/batch-edit", new
        {
            environmentSetIds = new[] { a.Id },
            changes = new object[] { new { operation = "Delete", kind = "EnvironmentVariable", targetName = "PROXMOX_VE_ENDPOINT" } }
        }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, change);
        await AssertStatus(HttpStatusCode.OK, remove);
    }

    [Fact]
    public async Task BatchEdit_refuses_a_secret_Terraform_variable()
    {
        using var secret = new SecretEnvironmentVariable("s3cr3t-token-value");
        var a = TestData.EnvironmentSet();
        await Seed(a);

        var response = await RootClient.PostAsJsonAsync($"{Sets}/batch-edit", new
        {
            environmentSetIds = new[] { a.Id },
            changes = new object[] { new { operation = "Upsert", kind = "TerraformVariable", targetName = "password", valueSource = "Secret", secretName = secret.Name } }
        }, Ct);

        await AssertProblem(HttpStatusCode.BadRequest, response);
    }

    // ---- connections and presets ---------------------------------------------------------------

    [Fact]
    public async Task GetConnections_lists_each_connections_fields_and_presets_without_values()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageEnvironmentSets).SeedAsync();

        var response = await Client(actor).GetAsync($"{Sets}/connections", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        var body = await response.Content.ReadAsStringAsync(Ct);
        var connections = await ReadAsync<ConnectionView[]>(response);
        var proxmox = connections.Single(x => x.Name == TestConfiguration.ProxmoxConnection);
        Assert.Contains(proxmox.Fields, x => x.Field == ConnectionField.ApiTokenSecret && x.IsSecret && x.IsAvailable);
        Assert.DoesNotContain(proxmox.Fields, x => x.Field == ConnectionField.Datacenter);
        Assert.Equal(["bpg/proxmox", "telmate/proxmox"], proxmox.Presets.Select(x => x.Id));
        Assert.Contains(proxmox.Presets[0].Entries, x => x.TargetName == "PROXMOX_VE_API_TOKEN");
        var vsphere = connections.Single(x => x.Name == TestConfiguration.VsphereConnection);
        Assert.Contains(vsphere.Fields, x => x.Field == ConnectionField.Datacenter && x.IsAvailable);
        Assert.DoesNotContain(TestConfiguration.ProxmoxApiToken, body);
        Assert.DoesNotContain(TestConfiguration.VspherePassword, body);
    }

    [Fact]
    public async Task CreateFromConnection_makes_the_presets_entries_and_needs_ManageInfrastructure()
    {
        var manager = await Actor().WithSystemPermissions(SystemPermission.ManageEnvironmentSets).SeedAsync();
        var command = new { connectionName = TestConfiguration.VsphereConnection, preset = "hashicorp/vsphere" };

        await AssertProblem(HttpStatusCode.Forbidden, await Client(manager).PostAsJsonAsync($"{Sets}/from-connection", command, Ct));

        var response = await RootClient.PostAsJsonAsync($"{Sets}/from-connection", command, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        var set = await ReadAsync<SetView>(response);
        Assert.Equal("lab-vcenter (hashicorp/vsphere)", set.Name);
        Assert.True(set.IsComplete);
        Assert.Equal(
            ["VSPHERE_ALLOW_UNVERIFIED_SSL", "VSPHERE_PASSWORD", "VSPHERE_SERVER", "VSPHERE_USER"],
            set.Entries.Select(x => x.TargetName).Order());
        Assert.All(set.Entries, x => Assert.Equal(EnvironmentSetValueSource.Connection, x.ValueSource));
    }

    [Fact]
    public async Task CreateFromConnection_refuses_another_providers_preset()
    {
        var response = await RootClient.PostAsJsonAsync(
            $"{Sets}/from-connection", new { connectionName = TestConfiguration.VsphereConnection, preset = "bpg/proxmox" }, Ct);

        await AssertProblem(HttpStatusCode.BadRequest, response);
    }
}
