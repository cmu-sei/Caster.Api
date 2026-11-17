// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Tests.Features.Workspaces;

/// <summary>What a workspace is answered with, and what PUT and PATCH store from their bodies.</summary>
public class WorkspaceMappingTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    [Fact]
    public async Task Reads_answer_every_member_of_the_workspace()
    {
        var workspace = await SeedWorkspace();

        var one = await ReadAsync<JsonElement>(await RootClient.GetAsync($"api/workspaces/{workspace.Id}", Ct));
        var all = await ReadAsync<JsonElement[]>(await RootClient.GetAsync("api/workspaces", Ct));
        var byDirectory = await ReadAsync<JsonElement[]>(await RootClient.GetAsync($"api/directories/{workspace.DirectoryId}/workspaces", Ct));

        foreach (var json in new[] { one, Assert.Single(all), Assert.Single(byDirectory) })
        {
            Assert.Equal(workspace.Id, json.GetProperty("id").GetGuid());
            Assert.Equal("seeded", json.GetProperty("name").GetString());
            Assert.Equal(workspace.DirectoryId, json.GetProperty("directoryId").GetGuid());
            Assert.True(json.GetProperty("dynamicHost").GetBoolean());
            Assert.Equal("1.1.1", json.GetProperty("terraformVersion").GetString());
            Assert.Equal(3, json.GetProperty("parallelism").GetInt32());
            Assert.Equal(6, json.GetProperty("azureDestroyFailureThreshold").GetInt32());
        }
    }

    /// <summary>A PUT writes every member: what its body leaves out is cleared.</summary>
    [Fact]
    public async Task Put_clears_what_the_body_leaves_out()
    {
        var workspace = await SeedWorkspace();

        var json = await ReadAsync<JsonElement>(await RootClient.PutAsJsonAsync($"api/workspaces/{workspace.Id}", new { name = "put" }, Ct));

        var stored = await ReadBack(db => db.Workspaces.SingleAsync(x => x.Id == workspace.Id, Ct));
        Assert.Equal(("put", false, (string)null, (int?)null, (int?)null),
            (stored.Name, stored.DynamicHost, stored.TerraformVersion, stored.Parallelism, stored.AzureDestroyFailureThreshold));
        Assert.Equal(JsonValueKind.Null, json.GetProperty("parallelism").ValueKind);
    }

    /// <summary>A PATCH keeps what its body leaves out.</summary>
    [Fact]
    public async Task Patch_keeps_what_the_body_leaves_out()
    {
        var workspace = await SeedWorkspace();

        await AssertStatus(HttpStatusCode.OK, await RootClient.PatchAsJsonAsync($"api/workspaces/{workspace.Id}", new { }, Ct));

        await AssertStored(workspace, ("seeded", true, "1.1.1", 3, 6));
    }

    /// <summary>A PATCH writes the members its body sends.</summary>
    [Fact]
    public async Task Patch_writes_what_the_body_sends()
    {
        var workspace = await SeedWorkspace();

        await AssertStatus(HttpStatusCode.OK, await RootClient.PatchAsJsonAsync($"api/workspaces/{workspace.Id}",
            new { name = "patched", dynamicHost = false, terraformVersion = "1.5.7", parallelism = 4, azureDestroyFailureThreshold = 2 }, Ct));

        await AssertStored(workspace, ("patched", false, "1.5.7", 4, 2));
    }

    /// <summary>An explicit null in a PATCH is skipped like an omitted member: parallelism and the threshold cannot be cleared this way.</summary>
    [Fact]
    public async Task Patch_with_explicit_nulls_keeps_every_member()
    {
        var workspace = await SeedWorkspace();

        await AssertStatus(HttpStatusCode.OK, await RootClient.PatchAsJsonAsync($"api/workspaces/{workspace.Id}",
            new { name = (string)null, dynamicHost = (bool?)null, terraformVersion = (string)null, parallelism = (int?)null, azureDestroyFailureThreshold = (int?)null }, Ct));

        await AssertStored(workspace, ("seeded", true, "1.1.1", 3, 6));
    }

    /// <summary>A create stores the members of its body.</summary>
    [Fact]
    public async Task Create_stores_the_members_of_the_body()
    {
        var workspace = await SeedWorkspace();

        var json = await ReadAsync<JsonElement>(await RootClient.PostAsJsonAsync("api/workspaces", new
        {
            name = "created",
            directoryId = workspace.DirectoryId,
            dynamicHost = true,
            terraformVersion = "0.12.29",
            parallelism = 9,
            azureDestroyFailureThreshold = 1
        }, Ct));

        var id = json.GetProperty("id").GetGuid();
        var stored = await ReadBack(db => db.Workspaces.SingleAsync(x => x.Id == id, Ct));
        Assert.Equal(("created", workspace.DirectoryId, true, "0.12.29", (int?)9, (int?)1),
            (stored.Name, stored.DirectoryId, stored.DynamicHost, stored.TerraformVersion, stored.Parallelism, stored.AzureDestroyFailureThreshold));
        Assert.Equal(9, json.GetProperty("parallelism").GetInt32());
    }

    private async Task AssertStored(Workspace workspace, (string Name, bool DynamicHost, string TerraformVersion, int? Parallelism, int? Threshold) expected)
    {
        var stored = await ReadBack(db => db.Workspaces.SingleAsync(x => x.Id == workspace.Id, Ct));
        Assert.Equal(expected, (stored.Name, stored.DynamicHost, stored.TerraformVersion, stored.Parallelism, stored.AzureDestroyFailureThreshold));
    }

    private async Task<Workspace> SeedWorkspace()
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        var workspace = TestData.Workspace(directory, "seeded");
        workspace.DynamicHost = true;
        workspace.TerraformVersion = "1.1.1";
        workspace.Parallelism = 3;
        workspace.AzureDestroyFailureThreshold = 6;
        await Seed(project, directory, workspace);

        return workspace;
    }
}
