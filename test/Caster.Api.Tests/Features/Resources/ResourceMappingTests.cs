// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;
using File = System.IO.File;

namespace Caster.Api.Tests.Features.Resources;

/// <summary>
/// What a workspace's resources are answered with: the list leaves each resource's attributes out, a single
/// resource carries them, and both carry the searchable attributes read from them.
/// </summary>
public class ResourceMappingTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    /// <summary>A vSphere virtual machine in <c>Data/terraform.tfstate</c>.</summary>
    private const string MachineAddress = "vsphere_virtual_machine.course-centos7-server";

    [Fact]
    public async Task The_resource_list_leaves_the_attributes_out()
    {
        var workspace = await SeedWorkspace();

        var resources = await ReadAsync<JsonElement[]>(await RootClient.GetAsync($"api/workspaces/{workspace.Id}/resources", Ct));

        Assert.NotEmpty(resources);
        Assert.All(resources, x => Assert.Equal(JsonValueKind.Null, x.GetProperty("attributes").ValueKind));
        AssertMachine(Assert.Single(resources, x => x.GetProperty("address").GetString() == MachineAddress));
        var portGroup = resources.First(x => x.GetProperty("type").GetString() == "vsphere_host_port_group");
        Assert.Equal(0, portGroup.GetProperty("searchableAttributes").GetProperty("vlan_id").GetInt32());
        var vSwitch = resources.First(x => x.GetProperty("type").GetString() == "vsphere_host_virtual_switch");
        Assert.Empty(vSwitch.GetProperty("searchableAttributes").EnumerateObject());
    }

    [Fact]
    public async Task A_single_resource_carries_its_attributes()
    {
        var workspace = await SeedWorkspace();

        var json = await ReadAsync<JsonElement>(await RootClient.GetAsync(
            $"api/workspaces/{workspace.Id}/resources/{Uri.EscapeDataString(MachineAddress)}", Ct));

        AssertMachine(json);
        Assert.Equal(JsonValueKind.Object, json.GetProperty("attributes").ValueKind);
        Assert.Equal("423c3932-665f-5764-ce9a-aa4a8d8a2023", json.GetProperty("attributes").GetProperty("id").GetString());
        Assert.Equal("rhel7_64Guest", json.GetProperty("attributes").GetProperty("guest_id").GetString());
    }

    /// <summary>An address not in the state is answered with no resource: a 204.</summary>
    [Fact]
    public async Task An_address_not_in_the_state_is_answered_with_no_content()
    {
        var workspace = await SeedWorkspace();

        await AssertStatus(HttpStatusCode.NoContent, await RootClient.GetAsync($"api/workspaces/{workspace.Id}/resources/nothing.here", Ct));
    }

    private static void AssertMachine(JsonElement json)
    {
        Assert.Equal("423c3932-665f-5764-ce9a-aa4a8d8a2023", json.GetProperty("id").GetString());
        Assert.Equal("course.centos7.server-4c2eb68c-a77f-45aa-990a-6b837ee59d71", json.GetProperty("name").GetString());
        Assert.Equal("vsphere_virtual_machine", json.GetProperty("type").GetString());
        Assert.Equal(MachineAddress, json.GetProperty("baseAddress").GetString());
        Assert.Equal(MachineAddress, json.GetProperty("address").GetString());
        Assert.False(json.GetProperty("tainted").GetBoolean());
        var searchable = json.GetProperty("searchableAttributes");
        Assert.Equal(0, searchable.GetProperty("guest_ip_addresses").GetArrayLength());
        Assert.Equal("rhel7_64Guest", searchable.GetProperty("guest_id").GetString());
    }

    /// <summary>A workspace whose state is <c>Data/terraform.tfstate</c>.</summary>
    private async Task<Workspace> SeedWorkspace()
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        var workspace = TestData.Workspace(directory);
        workspace.State = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Data", "terraform.tfstate"), Ct);
        await Seed(project, directory, workspace);

        return workspace;
    }
}
