// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using ProjectSetView = Caster.Api.Features.EnvironmentSets.ProjectEnvironmentSet;
using SetView = Caster.Api.Features.EnvironmentSets.EnvironmentSet;

namespace Caster.Api.Tests.Features.EnvironmentSets;

/// <summary>
/// Entries whose value comes from an infrastructure connection. They hand out a hypervisor's settings,
/// so creating or editing one also needs ManageInfrastructure; members see which connection a
/// non-secret field comes from, and only whether a credential is available. The connections are
/// <see cref="TestConfiguration"/>'s.
/// </summary>
public class EnvironmentSetConnectionRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    private const string Sets = "api/environment-sets";

    private static object ConnectionEntry(string targetName, string field, string kind = "EnvironmentVariable", string connection = TestConfiguration.ProxmoxConnection) =>
        new { kind, targetName, valueSource = "Connection", connectionName = connection, connectionField = field };

    [Fact]
    public async Task CreateEntry_with_ManageEnvironmentSets_and_ManageInfrastructure_stores_a_connection_entry()
    {
        var set = TestData.EnvironmentSet();
        await Seed(set);
        var actor = await Actor()
            .WithSystemPermissions(SystemPermission.ManageEnvironmentSets, SystemPermission.ManageInfrastructure)
            .SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"{Sets}/{set.Id}/entries", ConnectionEntry("PROXMOX_VE_API_TOKEN", "ApiToken", connection: " LAB-PVE "), Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        var entry = Assert.Single((await ReadAsync<SetView>(response)).Entries);
        Assert.True(entry.IsAvailable, entry.Unavailable);
        Assert.True(entry.IsSecret);
        Assert.Equal(ConnectionField.ApiToken, entry.ConnectionField);
        var stored = await ReadBack(db => db.EnvironmentSetEntries.SingleAsync(x => x.EnvironmentSetId == set.Id, Ct));
        Assert.Equal("LAB-PVE", stored.ConnectionName);
        Assert.Null(stored.Value);
        Assert.DoesNotContain(TestConfiguration.ProxmoxApiToken, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task CreateEntry_of_a_connection_entry_is_forbidden_without_ManageInfrastructure()
    {
        var set = TestData.EnvironmentSet();
        await Seed(set);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageEnvironmentSets).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"{Sets}/{set.Id}/entries", ConnectionEntry("PROXMOX_VE_ENDPOINT", "Url"), Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task EditEntry_of_a_connection_entry_into_a_plain_one_is_forbidden_without_ManageInfrastructure()
    {
        var set = TestData.EnvironmentSet();
        var entry = TestData.ConnectionEntry(set, "PROXMOX_VE_ENDPOINT", TestConfiguration.ProxmoxConnection, ConnectionField.Url);
        await Seed(set, entry);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageEnvironmentSets).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync(
            $"{Sets}/{set.Id}/entries/{entry.Id}",
            new { kind = "EnvironmentVariable", targetName = "PROXMOX_VE_ENDPOINT", valueSource = "Plain", value = "https://elsewhere" },
            Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Theory]
    [InlineData("nowhere", "Url", "There is no infrastructure connection named")]
    [InlineData(TestConfiguration.VsphereConnection, "ApiToken", "ApiToken doesn't apply to vsphere connections.")]
    [InlineData(TestConfiguration.ProxmoxConnection, "Datacenter", "Datacenter doesn't apply to proxmox connections.")]
    public async Task CreateEntry_refuses_a_connection_or_field_that_does_not_fit(string connection, string field, string message)
    {
        var set = TestData.EnvironmentSet();
        await Seed(set);

        var response = await RootClient.PostAsJsonAsync($"{Sets}/{set.Id}/entries", ConnectionEntry("TARGET", field, connection: connection), Ct);

        await AssertProblem(HttpStatusCode.BadRequest, response);
        Assert.Contains(message, (await response.Content.ReadAsStringAsync(Ct)).Replace("\\u0027", "'"));
    }

    [Fact]
    public async Task CreateEntry_refuses_a_connection_credential_as_a_Terraform_variable_but_takes_a_plain_field()
    {
        var set = TestData.EnvironmentSet();
        await Seed(set);

        var credential = await RootClient.PostAsJsonAsync($"{Sets}/{set.Id}/entries", ConnectionEntry("pm_password", "ApiTokenSecret", "TerraformVariable"), Ct);
        var url = await RootClient.PostAsJsonAsync($"{Sets}/{set.Id}/entries", ConnectionEntry("pm_api_url", "ApiUrl", "TerraformVariable"), Ct);

        await AssertProblem(HttpStatusCode.BadRequest, credential);
        await AssertStatus(HttpStatusCode.OK, url);
    }

    [Fact]
    public async Task A_connection_entry_whose_connection_has_left_configuration_shows_as_unavailable()
    {
        var set = TestData.EnvironmentSet();
        await Seed(set, TestData.ConnectionEntry(set, "PROXMOX_VE_ENDPOINT", "renamed-away", ConnectionField.Url));

        var view = await ReadAsync<SetView>(await RootClient.GetAsync($"{Sets}/{set.Id}", Ct));

        var entry = Assert.Single(view.Entries);
        Assert.False(entry.IsAvailable);
        Assert.Contains("\"renamed-away\" is not configured", entry.Unavailable);
        Assert.False(view.IsComplete);
    }

    [Fact]
    public async Task GetByProject_shows_members_the_connection_of_a_plain_field_and_only_whether_a_credential_is_available()
    {
        var set = TestData.EnvironmentSet();
        var project = TestData.Project();
        await Seed(
            set, project,
            TestData.ConnectionEntry(set, "VSPHERE_SERVER", TestConfiguration.VsphereConnection, ConnectionField.Host),
            TestData.ConnectionEntry(set, "VSPHERE_PASSWORD", TestConfiguration.VsphereConnection, ConnectionField.Password),
            TestData.ProjectEnvironmentSet(project, set));
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/projects/{project.Id}/environment-sets", Ct);

        var body = await response.Content.ReadAsStringAsync(Ct);
        var view = Assert.Single(await ReadAsync<ProjectSetView[]>(response));
        var server = view.Entries.Single(x => x.TargetName == "VSPHERE_SERVER");
        Assert.Equal(TestConfiguration.VsphereConnection, server.ConnectionName);
        Assert.Equal(ConnectionField.Host, server.ConnectionField);
        var password = view.Entries.Single(x => x.TargetName == "VSPHERE_PASSWORD");
        Assert.True(password.IsSecret);
        Assert.True(password.IsAvailable);
        Assert.Null(password.ConnectionName);
        Assert.Null(password.ConnectionField);
        Assert.DoesNotContain(TestConfiguration.VspherePassword, body);
        Assert.DoesNotContain("vcenter.test", body);
    }

    /// <summary>ManageInfrastructure implies ViewInfrastructure, so its holder can see the connections they assign.</summary>
    [Fact]
    public async Task ManageInfrastructure_alone_can_list_the_inventory_connections()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageInfrastructure).SeedAsync();

        var response = await Client(actor).GetAsync("api/inventory/connections", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
    }

    [Theory]
    [InlineData("GIT_CONFIG_COUNT")]
    [InlineData("GIT_CONFIG_KEY_0")]
    [InlineData("git_config_value_12")]
    public async Task CreateEntry_refuses_gits_numbered_configuration(string targetName)
    {
        var set = TestData.EnvironmentSet();
        await Seed(set);

        var response = await RootClient.PostAsJsonAsync(
            $"{Sets}/{set.Id}/entries", new { kind = "EnvironmentVariable", targetName, valueSource = "Plain", value = "x" }, Ct);

        await AssertProblem(HttpStatusCode.BadRequest, response);
    }
}
