// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Files;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Directory = Caster.Api.Domain.Models.Directory;
using File = Caster.Api.Domain.Models.File;
using FileView = Caster.Api.Features.Files.File;

namespace Caster.Api.Tests.Features.Files;

/// <summary>
/// The AI Terraform draft routes: drafting needs EditProject (or EditProjects) and AI enabled; applying saves
/// the reviewed files all together, or none of them when one has changed or is held by another user.
/// </summary>
public class AiTerraformDraftRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    [Fact]
    public async Task Draft_is_a_conflict_while_AI_is_not_enabled()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/directories/{directory.Id}/files/ai-draft", new { prompt = "add a vm" }, Ct);

        await AssertProblem(HttpStatusCode.Conflict, response);
    }

    [Fact]
    public async Task Draft_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/directories/{directory.Id}/files/ai-draft", new { prompt = "add a vm" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Apply_creates_a_new_file_with_a_version()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Apply(actor, directory, new { name = "network.tf", content = "resource \"a\" \"b\" {}" });

        var saved = Assert.Single(await ReadAsync<FileView[]>(response));
        var stored = await StoredFile(saved.Id);
        Assert.Equal("resource \"a\" \"b\" {}", stored.Content);
        Assert.Equal(directory.Id, stored.DirectoryId);
        Assert.Equal(1, await VersionCount(saved.Id));
    }

    [Fact]
    public async Task Apply_replaces_an_unlocked_file_and_leaves_it_unlocked()
    {
        var (project, directory) = await SeedDirectory();
        var file = await SeedFile(directory, "main.tf", "# old");
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Apply(actor, directory, Replace(file, "# new")));

        var stored = await StoredFile(file.Id);
        Assert.Equal("# new", stored.Content);
        Assert.Null(stored.LockedById);
        Assert.Equal(actor.Id, stored.ModifiedById);
    }

    [Fact]
    public async Task Apply_keeps_the_callers_own_lock()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        var file = await SeedFile(directory, "main.tf", "# old", lockedBy: actor);

        await AssertStatus(HttpStatusCode.OK, await Apply(actor, directory, Replace(file, "# new")));

        Assert.Equal(actor.Id, (await StoredFile(file.Id)).LockedById);
    }

    [Fact]
    public async Task Apply_saves_nothing_when_a_replaced_file_has_changed_since_the_draft()
    {
        var (project, directory) = await SeedDirectory();
        var file = await SeedFile(directory, "main.tf", "# changed by someone");
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Apply(actor, directory,
            new { name = "new.tf", content = "# new" },
            new { name = "main.tf", content = "# drafted", existingFileId = file.Id, existingContentHash = AiTerraformDraftBuilder.ContentHash("# what the draft saw") });

        await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.Equal("# changed by someone", (await StoredFile(file.Id)).Content);
        Assert.False(await FileNamed("new.tf"));
    }

    [Fact]
    public async Task Apply_saves_nothing_while_another_user_holds_a_replaced_file()
    {
        var (project, directory) = await SeedDirectory();
        var holder = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        var file = await SeedFile(directory, "main.tf", "# old", lockedBy: holder);
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Apply(actor, directory, Replace(file, "# new"));

        await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.Equal("# old", (await StoredFile(file.Id)).Content);
    }

    [Fact]
    public async Task Apply_refuses_a_new_file_whose_name_was_taken_since_the_draft()
    {
        var (project, directory) = await SeedDirectory();
        await SeedFile(directory, "network.tf", "# added meanwhile");
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Apply(actor, directory, new { name = "network.tf", content = "# drafted" });

        await AssertProblem(HttpStatusCode.Conflict, response);
    }

    [Fact]
    public async Task Apply_refuses_a_file_of_another_directory()
    {
        var (project, directory) = await SeedDirectory();
        var other = TestData.Directory(project, "other");
        await Seed(other);
        var file = await SeedFile(other, "main.tf", "# other");
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Apply(actor, directory, Replace(file, "# moved"));

        await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.Equal("# other", (await StoredFile(file.Id)).Content);
    }

    [Fact]
    public async Task Apply_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Apply(actor, directory, new { name = "refused.tf", content = "# no" });

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.False(await FileNamed("refused.tf"));
    }

    [Fact]
    public async Task Apply_refuses_two_files_of_one_name()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Apply(actor, directory, new { name = "a.tf", content = "# 1" }, new { name = "a.tf", content = "# 2" });

        await AssertProblem(HttpStatusCode.BadRequest, response);
        Assert.False(await FileNamed("a.tf"));
    }

    private Task<HttpResponseMessage> Apply(TestActor actor, Directory directory, params object[] files) =>
        Client(actor).PostAsJsonAsync($"api/directories/{directory.Id}/files/ai-draft/apply", new { files }, Ct);

    private static object Replace(File file, string content) => new
    {
        name = file.Name,
        content,
        existingFileId = file.Id,
        existingContentHash = AiTerraformDraftBuilder.ContentHash(file.Content)
    };

    private async Task<(Project Project, Directory Directory)> SeedDirectory()
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        await Seed(project, directory);

        return (project, directory);
    }

    private async Task<File> SeedFile(Directory directory, string name, string content, TestActor lockedBy = null)
    {
        var file = TestData.File(directory, name, content);
        if (lockedBy != null)
            file.Lock(lockedBy.Id, canLock: false);
        await Seed(file);

        return file;
    }

    private async Task<File> StoredFile(Guid id)
    {
        await using var context = NewContext();

        return await context.Files.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == id, Ct);
    }

    private async Task<bool> FileNamed(string name)
    {
        await using var context = NewContext();

        return await context.Files.AnyAsync(x => x.Name == name, Ct);
    }

    private async Task<int> VersionCount(Guid fileId)
    {
        await using var context = NewContext();

        return await context.FileVersions.CountAsync(x => x.FileId == fileId, Ct);
    }
}
