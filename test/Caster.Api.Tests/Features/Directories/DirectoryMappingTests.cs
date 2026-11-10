// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Hubs;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Directory = Caster.Api.Domain.Models.Directory;
using DirectoryView = Caster.Api.Features.Directories.Directory;
using File = Caster.Api.Domain.Models.File;

namespace Caster.Api.Tests.Features.Directories;

/// <summary>
/// What a directory is answered with: its files, workspaces and designs only when a read asks for them (the
/// file content only when asked as well), empty lists after a write, and what each kind of edit stores.
/// </summary>
public class DirectoryMappingTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    // ---- Reads ------------------------------------------------------------------------------------

    /// <summary>Without includeRelated a read leaves files, workspaces and designs null, even when the file content is asked for.</summary>
    [Fact]
    public async Task A_read_without_includeRelated_leaves_the_related_lists_null()
    {
        var graph = await SeedGraph();

        foreach (var query in new[] { "", "?includeFileContent=true" })
        {
            var json = await ReadAsync<JsonElement>(await RootClient.GetAsync($"api/directories/{graph.Directory.Id}{query}", Ct));

            AssertDirectoryMembers(json, graph.Directory);
            Assert.Equal(JsonValueKind.Null, json.GetProperty("files").ValueKind);
            Assert.Equal(JsonValueKind.Null, json.GetProperty("workspaces").ValueKind);
            Assert.Equal(JsonValueKind.Null, json.GetProperty("designs").ValueKind);
        }
    }

    /// <summary>
    /// includeRelated adds the files (without content), the workspaces and the designs (without their modules);
    /// includeFileContent adds the content.
    /// </summary>
    [Fact]
    public async Task A_read_with_includeRelated_carries_the_files_workspaces_and_designs()
    {
        var graph = await SeedGraph();

        var related = await ReadAsync<JsonElement>(await RootClient.GetAsync($"api/directories/{graph.Directory.Id}?includeRelated=true", Ct));
        var withContent = await ReadAsync<JsonElement>(await RootClient.GetAsync(
            $"api/directories/{graph.Directory.Id}?includeRelated=true&includeFileContent=true", Ct));

        AssertRelated(related, graph, content: null);
        AssertRelated(withContent, graph, content: "content-a");
    }

    /// <summary>The list reads expand the same way as a single read.</summary>
    [Fact]
    public async Task The_directory_lists_expand_as_a_single_read_does()
    {
        var graph = await SeedGraph();

        var all = await ReadAsync<JsonElement[]>(await RootClient.GetAsync("api/directories?includeRelated=true&includeFileContent=true", Ct));
        var byProject = await ReadAsync<JsonElement[]>(await RootClient.GetAsync($"api/projects/{graph.Project.Id}/directories?includeRelated=true", Ct));
        var children = await ReadAsync<JsonElement[]>(await RootClient.GetAsync($"api/directories/{graph.Directory.Id}/children", Ct));
        var plain = await ReadAsync<JsonElement[]>(await RootClient.GetAsync("api/directories", Ct));

        AssertRelated(Assert.Single(all, x => x.GetProperty("id").GetGuid() == graph.Directory.Id), graph, content: "content-a");
        AssertRelated(Assert.Single(byProject), graph, content: null);
        var child = Assert.Single(children);
        Assert.Equal(graph.Child.Id, child.GetProperty("id").GetGuid());
        Assert.Equal(graph.Directory.Id, child.GetProperty("parentId").GetGuid());
        Assert.Equal(JsonValueKind.Null, child.GetProperty("files").ValueKind);
        Assert.All(plain, x => Assert.Equal(JsonValueKind.Null, x.GetProperty("designs").ValueKind));
    }

    // ---- Writes -----------------------------------------------------------------------------------

    /// <summary>A write answers the directory with empty related lists, where a read without includeRelated has nulls.</summary>
    [Fact]
    public async Task Writes_answer_empty_related_lists()
    {
        var graph = await SeedGraph();

        var created = await ReadAsync<JsonElement>(await RootClient.PostAsJsonAsync(
            "api/directories", new { name = "created", projectId = graph.Project.Id }, Ct));
        var put = await ReadAsync<JsonElement>(await RootClient.PutAsJsonAsync(
            $"api/directories/{graph.Directory.Id}", new { name = "put" }, Ct));
        var patched = await ReadAsync<JsonElement>(await RootClient.PatchAsJsonAsync(
            $"api/directories/{graph.Directory.Id}", new { name = "patched" }, Ct));

        foreach (var json in new[] { created, put, patched })
        {
            Assert.Equal(0, json.GetProperty("files").GetArrayLength());
            Assert.Equal(0, json.GetProperty("workspaces").GetArrayLength());
            Assert.Equal(0, json.GetProperty("designs").GetArrayLength());
        }
    }

    /// <summary>A create keeps the id the body sends and stores every member of the body.</summary>
    [Fact]
    public async Task Create_stores_every_member_of_the_body_under_the_id_it_sends()
    {
        var graph = await SeedGraph();
        var id = Guid.NewGuid();

        var json = await ReadAsync<JsonElement>(await RootClient.PostAsJsonAsync("api/directories", new
        {
            id,
            name = "with-id",
            projectId = graph.Project.Id,
            parentId = graph.Directory.Id,
            terraformVersion = "1.5.7",
            parallelism = 2,
            azureDestroyFailureThreshold = 6,
            azureDestroyFailureThresholdEnabled = true
        }, Ct));

        Assert.Equal(id, json.GetProperty("id").GetGuid());
        var stored = await ReadBack(db => db.Directories.SingleAsync(x => x.Id == id, Ct));
        Assert.Equal(("with-id", (Guid?)graph.Directory.Id, "1.5.7", (int?)2, (int?)6, true),
            (stored.Name, stored.ParentId, stored.TerraformVersion, stored.Parallelism, stored.AzureDestroyFailureThreshold, stored.AzureDestroyFailureThresholdEnabled));
        Assert.Equal($"{graph.Directory.Path}{id}/", stored.Path);
    }

    /// <summary>A create without an id is given a new one.</summary>
    [Fact]
    public async Task Create_without_an_id_is_given_one()
    {
        var graph = await SeedGraph();

        var json = await ReadAsync<JsonElement>(await RootClient.PostAsJsonAsync("api/directories", new { name = "no-id", projectId = graph.Project.Id }, Ct));

        Assert.NotEqual(Guid.Empty, json.GetProperty("id").GetGuid());
    }

    /// <summary>A PUT writes every member: what the body leaves out is cleared, and a missing parent moves the directory to the top.</summary>
    [Fact]
    public async Task Put_clears_what_the_body_leaves_out()
    {
        var graph = await SeedGraph();

        await AssertStatus(HttpStatusCode.OK, await RootClient.PutAsJsonAsync($"api/directories/{graph.Child.Id}", new { name = "put" }, Ct));

        var stored = await ReadBack(db => db.Directories.SingleAsync(x => x.Id == graph.Child.Id, Ct));
        Assert.Equal(("put", (Guid?)null, (string)null, (int?)null, (int?)null, false),
            (stored.Name, stored.ParentId, stored.TerraformVersion, stored.Parallelism, stored.AzureDestroyFailureThreshold, stored.AzureDestroyFailureThresholdEnabled));
    }

    /// <summary>A PATCH keeps every member its body leaves out.</summary>
    [Fact]
    public async Task Patch_keeps_what_the_body_leaves_out()
    {
        var graph = await SeedGraph();

        var json = await ReadAsync<JsonElement>(await RootClient.PatchAsJsonAsync($"api/directories/{graph.Child.Id}", new { }, Ct));

        var stored = await ReadBack(db => db.Directories.SingleAsync(x => x.Id == graph.Child.Id, Ct));
        Assert.Equal(("child", (Guid?)graph.Directory.Id, "1.5.0", (int?)4, (int?)3, true),
            (stored.Name, stored.ParentId, stored.TerraformVersion, stored.Parallelism, stored.AzureDestroyFailureThreshold, stored.AzureDestroyFailureThresholdEnabled));
        Assert.Equal(4, json.GetProperty("parallelism").GetInt32());
    }

    /// <summary>A PATCH writes the members its body sends.</summary>
    [Fact]
    public async Task Patch_writes_what_the_body_sends()
    {
        var graph = await SeedGraph();
        var other = TestData.Directory(graph.Project, "other");
        await Seed(other);

        await AssertStatus(HttpStatusCode.OK, await RootClient.PatchAsJsonAsync($"api/directories/{graph.Child.Id}", new
        {
            name = "renamed",
            parentId = other.Id,
            terraformVersion = "1.5.7",
            parallelism = 7,
            azureDestroyFailureThreshold = 8,
            azureDestroyFailureThresholdEnabled = false
        }, Ct));

        var stored = await ReadBack(db => db.Directories.SingleAsync(x => x.Id == graph.Child.Id, Ct));
        Assert.Equal(("renamed", (Guid?)other.Id, "1.5.7", (int?)7, (int?)8, false),
            (stored.Name, stored.ParentId, stored.TerraformVersion, stored.Parallelism, stored.AzureDestroyFailureThreshold, stored.AzureDestroyFailureThresholdEnabled));
    }

    /// <summary>
    /// A PATCH with an explicit null parent moves the directory to the top, but an explicit null parallelism or
    /// destroy failure threshold is skipped like an omitted one: the stored value is kept.
    /// </summary>
    [Fact]
    public async Task Patch_with_explicit_nulls_detaches_the_parent_but_keeps_parallelism_and_threshold()
    {
        var graph = await SeedGraph();

        await AssertStatus(HttpStatusCode.OK, await RootClient.PatchAsJsonAsync($"api/directories/{graph.Child.Id}",
            new { parentId = (Guid?)null, parallelism = (int?)null, azureDestroyFailureThreshold = (int?)null, azureDestroyFailureThresholdEnabled = (bool?)null, terraformVersion = (string)null }, Ct));

        var stored = await ReadBack(db => db.Directories.SingleAsync(x => x.Id == graph.Child.Id, Ct));
        Assert.Equal(((Guid?)null, "1.5.0", (int?)4, (int?)3, true),
            (stored.ParentId, stored.TerraformVersion, stored.Parallelism, stored.AzureDestroyFailureThreshold, stored.AzureDestroyFailureThresholdEnabled));
    }

    /// <summary>The broadcast of a created or updated directory is read without the related lists.</summary>
    [Fact]
    public async Task The_directory_broadcasts_carry_no_related_lists()
    {
        var graph = await SeedGraph();

        await AssertStatus(HttpStatusCode.Created, await RootClient.PostAsJsonAsync("api/directories", new { name = "broadcast", projectId = graph.Project.Id }, Ct));
        await AssertStatus(HttpStatusCode.OK, await RootClient.PatchAsJsonAsync($"api/directories/{graph.Directory.Id}", new { parallelism = 9 }, Ct));

        var broadcasts = Factory.Hub<ProjectHub>().ToGroup(graph.Project.Id);
        var created = Assert.IsType<DirectoryView>(Assert.Single(broadcasts, x => x.Method == "DirectoryCreated").Arguments[0]);
        var updated = Assert.IsType<DirectoryView>(Assert.Single(broadcasts, x => x.Method == "DirectoryUpdated").Arguments[0]);
        Assert.Equal("broadcast", created.Name);
        Assert.Null(created.Files);
        Assert.Null(created.Workspaces);
        Assert.Null(created.Designs);
        Assert.Equal(9, updated.Parallelism);
        Assert.Null(updated.Files);
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    private static void AssertDirectoryMembers(JsonElement json, Directory directory)
    {
        Assert.Equal(directory.Id, json.GetProperty("id").GetGuid());
        Assert.Equal(directory.Name, json.GetProperty("name").GetString());
        Assert.Equal(directory.ProjectId, json.GetProperty("projectId").GetGuid());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("parentId").ValueKind);
        Assert.Equal("1.5.0", json.GetProperty("terraformVersion").GetString());
        Assert.Equal(4, json.GetProperty("parallelism").GetInt32());
        Assert.Equal(3, json.GetProperty("azureDestroyFailureThreshold").GetInt32());
        Assert.True(json.GetProperty("azureDestroyFailureThresholdEnabled").GetBoolean());
    }

    private void AssertRelated(JsonElement json, Graph graph, string content)
    {
        AssertDirectoryMembers(json, graph.Directory);

        var file = Assert.Single(json.GetProperty("files").EnumerateArray());
        Assert.Equal(graph.File.Id, file.GetProperty("id").GetGuid());
        Assert.Equal("Root", file.GetProperty("modifiedByName").GetString());
        Assert.Equal(graph.Holder.Name, file.GetProperty("lockedByName").GetString());
        Assert.True(file.GetProperty("administrativelyLocked").GetBoolean());
        if (content is null)
            Assert.Equal(JsonValueKind.Null, file.GetProperty("content").ValueKind);
        else
            Assert.Equal(content, file.GetProperty("content").GetString());

        var workspace = Assert.Single(json.GetProperty("workspaces").EnumerateArray());
        Assert.Equal(graph.Workspace.Id, workspace.GetProperty("id").GetGuid());
        Assert.Equal(graph.Directory.Id, workspace.GetProperty("directoryId").GetGuid());
        Assert.Equal("1.4.0", workspace.GetProperty("terraformVersion").GetString());
        Assert.Equal(2, workspace.GetProperty("parallelism").GetInt32());
        Assert.Equal(5, workspace.GetProperty("azureDestroyFailureThreshold").GetInt32());
        Assert.True(workspace.GetProperty("dynamicHost").GetBoolean());

        var design = Assert.Single(json.GetProperty("designs").EnumerateArray());
        Assert.Equal(graph.Design.Id, design.GetProperty("id").GetGuid());
        Assert.Equal(graph.Design.Name, design.GetProperty("name").GetString());
        Assert.Equal(graph.Directory.Id, design.GetProperty("directoryId").GetGuid());
        Assert.False(design.GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, design.GetProperty("modules").ValueKind);
    }

    private sealed record Graph(Project Project, Directory Directory, Directory Child, Workspace Workspace, File File, Design Design, TestActor Holder);

    /// <summary>
    /// A directory with settings of its own, one file (saved by Root, edit-locked by another user and
    /// administratively locked), one workspace, one disabled design with a module, and one child directory.
    /// </summary>
    private async Task<Graph> SeedGraph()
    {
        var holder = await Actor().WithName("Lock Holder").SeedAsync();
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        directory.TerraformVersion = "1.5.0";
        directory.Parallelism = 4;
        directory.AzureDestroyFailureThreshold = 3;
        directory.AzureDestroyFailureThresholdEnabled = true;
        var child = TestData.Directory(project, "child", directory);
        child.TerraformVersion = "1.5.0";
        child.Parallelism = 4;
        child.AzureDestroyFailureThreshold = 3;
        child.AzureDestroyFailureThresholdEnabled = true;
        var workspace = TestData.Workspace(directory);
        workspace.TerraformVersion = "1.4.0";
        workspace.Parallelism = 2;
        workspace.AzureDestroyFailureThreshold = 5;
        workspace.DynamicHost = true;
        var design = TestData.Design(directory);
        design.Enabled = false;
        var module = TestData.Module();
        await Seed(project, directory, child, workspace, design, module, TestData.DesignModule(design, module));
        var file = TestData.File(directory, "a.tf", "content-a");
        file.Save(Root.Id, canLock: true, bypassLock: true);
        file.Lock(holder.Id, canLock: true);
        file.AdministrativelyLock(canLock: true);
        await Seed(file);

        return new Graph(project, directory, child, workspace, file, design, holder);
    }
}
