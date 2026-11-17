// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Hubs;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Directory = Caster.Api.Domain.Models.Directory;
using File = Caster.Api.Domain.Models.File;
using FileView = Caster.Api.Features.Files.File;

namespace Caster.Api.Tests.Features.Files;

/// <summary>
/// What a file and its versions are answered with: every member of the response, which reads carry the
/// content, the names read through the user navigations, and what the edits store from their bodies.
/// </summary>
public class FileMappingTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    // ---- Reads ------------------------------------------------------------------------------------

    /// <summary>A file read on its own carries its content, the names of who saved and who holds its lock, and the administrative lock.</summary>
    [Fact]
    public async Task Get_answers_every_member_of_the_file()
    {
        var (directory, workspace, file, holder) = await SeedSavedLockedFile();

        var json = await ReadAsync<JsonElement>(await RootClient.GetAsync($"api/files/{file.Id}", Ct));

        AssertFile(json, file, directory, holder, content: "content-a");
        Assert.Equal(workspace.Id, json.GetProperty("workspaceId").GetGuid());
    }

    /// <summary>A file never saved has no saver and no lock: those members are null rather than missing.</summary>
    [Fact]
    public async Task Get_answers_null_names_for_a_file_nobody_saved_or_locked()
    {
        var (_, directory) = await SeedDirectory();
        var file = TestData.File(directory, "plain.tf", "plain");
        await Seed(file);

        var json = await ReadAsync<JsonElement>(await RootClient.GetAsync($"api/files/{file.Id}", Ct));

        Assert.Equal("plain", json.GetProperty("content").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("workspaceId").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("modifiedById").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("modifiedByName").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("dateSaved").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("lockedById").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("lockedByName").ValueKind);
        Assert.False(json.GetProperty("administrativelyLocked").GetBoolean());
    }

    /// <summary>The file lists carry the content only when asked, and the same other members as a single read.</summary>
    [Fact]
    public async Task GetAll_and_GetByDirectory_carry_the_content_only_when_asked()
    {
        var (directory, _, file, holder) = await SeedSavedLockedFile();

        var all = await ReadAsync<JsonElement[]>(await RootClient.GetAsync("api/files?includeContent=false", Ct));
        var allWithContent = await ReadAsync<JsonElement[]>(await RootClient.GetAsync("api/files?includeContent=true", Ct));
        var byDirectory = await ReadAsync<JsonElement[]>(await RootClient.GetAsync($"api/directories/{directory.Id}/files", Ct));
        var byDirectoryWithContent = await ReadAsync<JsonElement[]>(
            await RootClient.GetAsync($"api/directories/{directory.Id}/files?includeContent=true", Ct));

        AssertFile(Assert.Single(all, x => x.GetProperty("id").GetGuid() == file.Id), file, directory, holder, content: null);
        AssertFile(Assert.Single(allWithContent, x => x.GetProperty("id").GetGuid() == file.Id), file, directory, holder, content: "content-a");
        AssertFile(Assert.Single(byDirectory), file, directory, holder, content: null);
        AssertFile(Assert.Single(byDirectoryWithContent), file, directory, holder, content: "content-a");
    }

    /// <summary>A file's version list leaves the content out; a single version carries it. Both name who saved and who tagged.</summary>
    [Fact]
    public async Task File_versions_carry_the_content_only_when_read_one_at_a_time()
    {
        var (_, _, file, _) = await SeedSavedLockedFile();
        file.Tag("release", Root.Id, TestData.DefaultDateCreated);
        await Db.SaveChangesAsync(Ct);
        var tagged = file.FileVersions.Single(x => x.Tag == "release");

        var versions = await ReadAsync<JsonElement[]>(await RootClient.GetAsync($"api/files/{file.Id}/versions", Ct));
        var one = await ReadAsync<JsonElement>(await RootClient.GetAsync($"api/files/versions/{tagged.Id}", Ct));

        Assert.Equal(2, versions.Length);
        Assert.All(versions, x => Assert.Equal(JsonValueKind.Null, x.GetProperty("content").ValueKind));
        var untagged = Assert.Single(versions, x => x.GetProperty("tag").ValueKind == JsonValueKind.Null);
        Assert.Equal("Root", untagged.GetProperty("modifiedByName").GetString());
        Assert.Equal(JsonValueKind.Null, untagged.GetProperty("taggedById").ValueKind);
        Assert.Equal(JsonValueKind.Null, untagged.GetProperty("taggedByName").ValueKind);
        Assert.Equal(JsonValueKind.Null, untagged.GetProperty("dateTagged").ValueKind);
        AssertTaggedVersion(Assert.Single(versions, x => x.GetProperty("tag").ValueKind == JsonValueKind.String), file, tagged, content: null);
        AssertTaggedVersion(one, file, tagged, content: "content-a");
    }

    /// <summary>Tagging answers the tagged versions without their content.</summary>
    [Fact]
    public async Task Tag_answers_the_new_versions_without_their_content()
    {
        var (_, _, file, _) = await SeedSavedLockedFile();

        var versions = await ReadAsync<JsonElement[]>(await RootClient.PostAsJsonAsync(
            "api/files/actions/tag", new { tag = "release", fileIds = new[] { file.Id } }, Ct));

        var version = Assert.Single(versions);
        Assert.Equal(file.Id, version.GetProperty("fileId").GetGuid());
        Assert.Equal("release", version.GetProperty("tag").GetString());
        Assert.Equal(Root.Id, version.GetProperty("taggedById").GetGuid());
        Assert.Equal("Root", version.GetProperty("taggedByName").GetString());
        Assert.Equal("Root", version.GetProperty("modifiedByName").GetString());
        Assert.Equal(JsonValueKind.Null, version.GetProperty("content").ValueKind);
    }

    // ---- The request-scoped cache -----------------------------------------------------------------

    /// <summary>
    /// A file write's response is read through the request's file cache, which the broadcast of the change
    /// fills first. A broadcast that does not report the content fetches the file without it, so the
    /// response carries no content either, although the content is stored.
    /// </summary>
    [Fact]
    public async Task A_created_file_is_answered_without_its_content_because_the_broadcast_read_it_first()
    {
        var (project, directory) = await SeedDirectory();

        var json = await ReadAsync<JsonElement>(await RootClient.PostAsJsonAsync(
            "api/files", new { name = "created.tf", content = "created", directoryId = directory.Id }, Ct));

        Assert.Equal("created.tf", json.GetProperty("name").GetString());
        Assert.Equal(directory.Id, json.GetProperty("directoryId").GetGuid());
        Assert.Equal("Root", json.GetProperty("modifiedByName").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("content").ValueKind);
        var id = json.GetProperty("id").GetGuid();
        Assert.Equal("created", (await ReadBack(db => db.Files.SingleAsync(x => x.Id == id, Ct))).Content);
        var broadcast = Assert.Single(Factory.Hub<ProjectHub>().ToGroup(project.Id), x => x.Method == "FileCreated");
        Assert.Null(Assert.IsType<FileView>(broadcast.Arguments[0]).Content);
    }

    /// <summary>As for a create: the lock's broadcast reports only the lock, so the response has no content.</summary>
    [Fact]
    public async Task A_locked_file_is_answered_without_its_content()
    {
        var (_, directory) = await SeedDirectory();
        var file = TestData.File(directory, "locked.tf", "kept");
        await Seed(file);

        var locked = await ReadAsync<JsonElement>(await RootClient.PostAsync($"api/files/{file.Id}/actions/lock", null, Ct));
        var read = await ReadAsync<JsonElement>(await RootClient.GetAsync($"api/files/{file.Id}", Ct));

        Assert.Equal(Root.Id, locked.GetProperty("lockedById").GetGuid());
        Assert.Equal("Root", locked.GetProperty("lockedByName").GetString());
        Assert.Equal(JsonValueKind.Null, locked.GetProperty("content").ValueKind);
        Assert.Equal("kept", read.GetProperty("content").GetString());
    }

    /// <summary>A PATCH that changes the content is broadcast with it, so its response carries the content.</summary>
    [Fact]
    public async Task A_patched_content_is_answered()
    {
        var (_, directory) = await SeedDirectory();
        var file = TestData.File(directory, "patched.tf", "old");
        file.Lock(Root.Id, canLock: true);
        await Seed(file);

        var json = await ReadAsync<JsonElement>(await RootClient.PatchAsJsonAsync($"api/files/{file.Id}", new { content = "new" }, Ct));

        Assert.Equal("new", json.GetProperty("content").GetString());
        Assert.Equal("patched.tf", json.GetProperty("name").GetString());
    }

    // ---- Writes -----------------------------------------------------------------------------------

    /// <summary>A create stores the directory and the workspace the body names (both are private setters on the entity).</summary>
    [Fact]
    public async Task Create_stores_the_directory_and_workspace_of_the_body()
    {
        var (_, directory) = await SeedDirectory();
        var workspace = TestData.Workspace(directory);
        await Seed(workspace);

        var json = await ReadAsync<JsonElement>(await RootClient.PostAsJsonAsync(
            "api/files", new { name = "in-workspace.tf", content = "x", directoryId = directory.Id, workspaceId = workspace.Id }, Ct));

        var id = json.GetProperty("id").GetGuid();
        var stored = await ReadBack(db => db.Files.SingleAsync(x => x.Id == id, Ct));
        Assert.Equal((directory.Id, (Guid?)workspace.Id, "in-workspace.tf", "x"), (stored.DirectoryId, stored.WorkspaceId, stored.Name, stored.Content));
        Assert.Equal(workspace.Id, json.GetProperty("workspaceId").GetGuid());
    }

    /// <summary>A PUT writes every member of its body: one without content clears the stored content.</summary>
    [Fact]
    public async Task Put_without_content_clears_the_content()
    {
        var (_, directory) = await SeedDirectory();
        var file = TestData.File(directory, "put.tf", "old");
        file.Lock(Root.Id, canLock: true);
        await Seed(file);

        var json = await ReadAsync<JsonElement>(await RootClient.PutAsJsonAsync($"api/files/{file.Id}", new { name = "renamed.tf" }, Ct));

        var stored = await ReadBack(db => db.Files.SingleAsync(x => x.Id == file.Id, Ct));
        Assert.Equal(("renamed.tf", (string)null), (stored.Name, stored.Content));
        Assert.Equal(JsonValueKind.Null, json.GetProperty("content").ValueKind);
    }

    /// <summary>A PATCH skips what its body leaves out: an omitted name and an omitted content are both kept.</summary>
    [Fact]
    public async Task Patch_keeps_an_omitted_name_and_an_omitted_content()
    {
        var (_, directory) = await SeedDirectory();
        var file = TestData.File(directory, "kept.tf", "kept content");
        file.Lock(Root.Id, canLock: true);
        await Seed(file);

        await AssertStatus(System.Net.HttpStatusCode.OK, await RootClient.PatchAsJsonAsync($"api/files/{file.Id}", new { }, Ct));
        var afterEmpty = await ReadBack(db => db.Files.SingleAsync(x => x.Id == file.Id, Ct));
        await AssertStatus(System.Net.HttpStatusCode.OK, await RootClient.PatchAsJsonAsync($"api/files/{file.Id}", new { name = "new.tf" }, Ct));
        var afterName = await ReadBack(db => db.Files.SingleAsync(x => x.Id == file.Id, Ct));

        Assert.Equal(("kept.tf", "kept content"), (afterEmpty.Name, afterEmpty.Content));
        Assert.Equal(("new.tf", "kept content"), (afterName.Name, afterName.Content));
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    private void AssertFile(JsonElement json, File file, Directory directory, TestActor holder, string content)
    {
        Assert.Equal(file.Id, json.GetProperty("id").GetGuid());
        Assert.Equal(file.Name, json.GetProperty("name").GetString());
        Assert.Equal(directory.Id, json.GetProperty("directoryId").GetGuid());
        if (content is null)
            Assert.Equal(JsonValueKind.Null, json.GetProperty("content").ValueKind);
        else
            Assert.Equal(content, json.GetProperty("content").GetString());
        Assert.Equal(Root.Id, json.GetProperty("modifiedById").GetGuid());
        Assert.Equal("Root", json.GetProperty("modifiedByName").GetString());
        Assert.Equal(file.DateSaved!.Value, json.GetProperty("dateSaved").GetDateTime().ToUniversalTime(), TimeSpan.FromMilliseconds(1));
        Assert.False(json.GetProperty("isDeleted").GetBoolean());
        Assert.Equal(holder.Id, json.GetProperty("lockedById").GetGuid());
        Assert.Equal(holder.Name, json.GetProperty("lockedByName").GetString());
        Assert.True(json.GetProperty("administrativelyLocked").GetBoolean());
    }

    private void AssertTaggedVersion(JsonElement json, File file, FileVersion tagged, string content)
    {
        Assert.Equal(tagged.Id, json.GetProperty("id").GetGuid());
        Assert.Equal(file.Id, json.GetProperty("fileId").GetGuid());
        Assert.Equal(file.Name, json.GetProperty("name").GetString());
        Assert.Equal(Root.Id, json.GetProperty("modifiedById").GetGuid());
        Assert.Equal("Root", json.GetProperty("modifiedByName").GetString());
        Assert.Equal("release", json.GetProperty("tag").GetString());
        Assert.Equal(Root.Id, json.GetProperty("taggedById").GetGuid());
        Assert.Equal("Root", json.GetProperty("taggedByName").GetString());
        Assert.Equal(TestData.DefaultDateCreated, json.GetProperty("dateTagged").GetDateTime().ToUniversalTime());
        if (content is null)
            Assert.Equal(JsonValueKind.Null, json.GetProperty("content").ValueKind);
        else
            Assert.Equal(content, json.GetProperty("content").GetString());
    }

    /// <summary>A file of a new directory and workspace, saved by Root, edit-locked by another user and administratively locked.</summary>
    private async Task<(Directory Directory, Workspace Workspace, File File, TestActor Holder)> SeedSavedLockedFile()
    {
        var holder = await Actor().WithName("Lock Holder").SeedAsync();
        var (_, directory) = await SeedDirectory();
        var workspace = TestData.Workspace(directory);
        await Seed(workspace);
        var file = TestData.File(directory, "a.tf", "content-a");
        file.Workspace = workspace;
        file.Save(Root.Id, canLock: true, bypassLock: true);
        file.Lock(holder.Id, canLock: true);
        file.AdministrativelyLock(canLock: true);
        await Seed(file);

        return (directory, workspace, file, holder);
    }

    private async Task<(Project Project, Directory Directory)> SeedDirectory()
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        await Seed(project, directory);

        return (project, directory);
    }
}
