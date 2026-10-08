// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Hubs;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Directory = Caster.Api.Domain.Models.Directory;
using File = Caster.Api.Domain.Models.File;
using FileVersionView = Caster.Api.Features.Files.FileVersion;
using FileView = Caster.Api.Features.Files.File;

namespace Caster.Api.Tests.Features.Files;

/// <summary>
/// <c>FilesController</c>: reading a file needs ViewProject on its project (or ViewProjects), changing it
/// EditProject (or EditProjects), forcing another user's lock off ManageProject, and the administrative lock
/// LockFiles. A file is edited only by the user holding its lock.
/// </summary>
public class FileRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    /// <summary>The title of the 409 <c>FileAdminLockedException</c> answers, which no other conflict on these routes carries.</summary>
    private const string AdminLockedTitle = "This File has been locked by an Administrator. It must be unlocked before you can make changes";

    /// <summary>The title of the 409 <c>File.VerifyLock</c> answers for a caller who does not hold the file's lock.</summary>
    private const string NotTheHolderTitle = "You cannot make changes to a File without holding it's lock";

    /// <summary>The title of the 409 <c>Rename</c> answers when another user holds the file's lock.</summary>
    private const string RenameWhileHeldTitle = "Cannot rename a file while it's being edited or locked by another user.";

    // ---- GET api/files/{id}, export, versions ---------------------------------------------------

    [Fact]
    public async Task Get_returns_the_file_to_a_member_holding_ViewProject()
    {
        var (project, _, file) = await SeedFile();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/files/{file.Id}", Ct);

        Assert.Equal(file.Content, (await ReadAsync<FileView>(response)).Content);
    }

    [Fact]
    public async Task Get_returns_the_file_to_a_caller_holding_ViewProjects()
    {
        var (_, _, file) = await SeedFile();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).GetAsync($"api/files/{file.Id}", Ct));
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var (project, _, file) = await SeedFile();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/files/{file.Id}", Ct));
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_ViewProject_only_on_another_project()
    {
        var (_, _, file) = await SeedFile();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/files/{file.Id}", Ct));
    }

    [Fact]
    public async Task Export_returns_the_content_as_text_to_a_member_holding_ViewProject()
    {
        var (project, _, file) = await SeedFile();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/files/{file.Id}/actions/export", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(file.Content, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Export_is_forbidden_for_a_caller_holding_ViewProject_only_on_another_project()
    {
        var (_, _, file) = await SeedFile();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/files/{file.Id}/actions/export", Ct));
    }

    [Fact]
    public async Task GetFileVersions_lists_the_versions_for_a_member_holding_ViewProject()
    {
        var (project, _, file) = await SeedFile();
        var version = TestData.FileVersion(file);
        await Seed(version);
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/files/{file.Id}/versions", Ct);

        Assert.Equal([version.Id], (await ReadAsync<FileVersionView[]>(response)).Select(x => x.Id));
    }

    [Fact]
    public async Task GetFileVersions_is_forbidden_for_a_caller_holding_ViewProject_only_on_another_project()
    {
        var (_, _, file) = await SeedFile();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/files/{file.Id}/versions", Ct));
    }

    [Fact]
    public async Task GetFileVersions_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var (project, _, file) = await SeedFile();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/files/{file.Id}/versions", Ct));
    }

    [Fact]
    public async Task GetFileVersion_returns_the_version_with_its_content_to_a_caller_holding_ViewProjects()
    {
        var (_, _, file) = await SeedFile();
        var version = TestData.FileVersion(file);
        await Seed(version);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var response = await Client(actor).GetAsync($"api/files/versions/{version.Id}", Ct);

        Assert.Equal(file.Content, (await ReadAsync<FileVersionView>(response)).Content);
    }

    /// <summary>A member holding ViewProject on the file's project is refused one of its versions.</summary>
    [Fact]
    public async Task GetFileVersion_is_forbidden_for_a_member_holding_ViewProject_on_the_files_project()
    {
        var (project, _, file) = await SeedFile();
        var version = TestData.FileVersion(file);
        await Seed(version);
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/files/versions/{version.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task GetFileVersion_is_forbidden_for_a_caller_holding_only_EditProjects()
    {
        var (_, _, file) = await SeedFile();
        var version = TestData.FileVersion(file);
        await Seed(version);
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/files/versions/{version.Id}", Ct));
    }

    // ---- GET api/files, GET api/directories/{id}/files ------------------------------------------

    [Fact]
    public async Task GetAll_lists_every_file_for_a_caller_holding_ViewProjects()
    {
        var (_, _, file) = await SeedFile();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var response = await Client(actor).GetAsync("api/files", Ct);

        Assert.Contains(file.Id, (await ReadAsync<FileView[]>(response)).Select(x => x.Id));
    }

    [Fact]
    public async Task GetAll_is_forbidden_for_a_member_holding_ViewProject_on_a_project()
    {
        var (project, _, _) = await SeedFile();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync("api/files", Ct));
    }

    [Fact]
    public async Task GetByDirectory_lists_the_directorys_files_for_a_member_holding_ViewProject()
    {
        var (project, directory, file) = await SeedFile();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/directories/{directory.Id}/files", Ct);

        Assert.Equal([file.Id], (await ReadAsync<FileView[]>(response)).Select(x => x.Id));
    }

    [Fact]
    public async Task GetByDirectory_lists_the_files_for_a_caller_holding_ViewProjects()
    {
        var (_, directory, _) = await SeedFile();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).GetAsync($"api/directories/{directory.Id}/files", Ct));
    }

    [Fact]
    public async Task GetByDirectory_is_forbidden_for_a_caller_holding_ViewProject_only_on_another_project()
    {
        var (_, directory, _) = await SeedFile();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/directories/{directory.Id}/files", Ct));
    }

    [Fact]
    public async Task GetByDirectory_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var (project, directory, _) = await SeedFile();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/directories/{directory.Id}/files", Ct));
    }

    // ---- POST api/files -------------------------------------------------------------------------

    [Fact]
    public async Task Create_by_a_member_holding_EditProject_stores_the_file_and_its_first_version()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(
            "api/files", new { name = "variables.tf", directoryId = directory.Id, content = "variable \"a\" {}" }, Ct);

        var created = await ReadAsync<FileView>(response);
        await using var context = NewContext();
        var stored = await context.Files.Include(x => x.FileVersions).SingleAsync(x => x.Id == created.Id, Ct);
        Assert.Equal("variable \"a\" {}", stored.Content);
        Assert.Equal(actor.Id, stored.ModifiedById);
        Assert.Single(stored.FileVersions);
    }

    [Fact]
    public async Task Create_by_a_caller_holding_EditProjects_stores_the_file()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/files", new { name = "main.tf", directoryId = directory.Id, content = "" }, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/files", new { name = "refused.tf", directoryId = directory.Id, content = "" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.False(await FileNamed("refused.tf"));
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/files", new { name = "refused.tf", directoryId = directory.Id, content = "" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.False(await FileNamed("refused.tf"));
    }

    [Fact]
    public async Task Create_broadcasts_the_file_to_its_projects_group()
    {
        var (project, directory) = await SeedDirectory();

        var response = await RootClient.PostAsJsonAsync("api/files", new { name = "main.tf", directoryId = directory.Id, content = "" }, Ct);

        var created = await ReadAsync<FileView>(response);
        var broadcast = Assert.Single(Factory.Hub<ProjectHub>().ToGroup(project.Id), x => x.Method == "FileCreated");
        Assert.Equal(created.Id, Assert.IsType<FileView>(broadcast.Arguments[0]).Id);
    }

    // ---- PUT/PATCH api/files/{id}, rename -------------------------------------------------------

    [Fact]
    public async Task Edit_by_the_member_holding_the_lock_stores_the_new_content()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        var file = await SeedFileLockedBy(directory, actor);

        var response = await Client(actor).PutAsJsonAsync(
            $"api/files/{file.Id}", new { name = file.Name, directoryId = directory.Id, content = "edited" }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("edited", (await StoredFile(file.Id)).Content);
    }

    [Fact]
    public async Task Edit_by_a_caller_holding_EditProjects_and_the_lock_stores_the_new_content()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();
        var file = await SeedFileLockedBy(directory, actor);

        var response = await Client(actor).PutAsJsonAsync(
            $"api/files/{file.Id}", new { name = file.Name, directoryId = directory.Id, content = "edited" }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
    }

    [Fact]
    public async Task Edit_without_holding_the_lock_is_a_conflict()
    {
        var (project, directory, file) = await SeedFile();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync(
            $"api/files/{file.Id}", new { name = file.Name, directoryId = directory.Id, content = "edited" }, Ct);

        var problem = await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.Equal(NotTheHolderTitle, problem.Title);
        Assert.Equal(file.Content, (await StoredFile(file.Id)).Content);
    }

    [Fact]
    public async Task Edit_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();
        var file = await SeedFileLockedBy(directory, actor);

        var response = await Client(actor).PutAsJsonAsync(
            $"api/files/{file.Id}", new { name = file.Name, directoryId = directory.Id, content = "edited" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(file.Content, (await StoredFile(file.Id)).Content);
    }

    [Fact]
    public async Task Edit_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();
        var file = await SeedFileLockedBy(directory, actor);

        var response = await Client(actor).PutAsJsonAsync(
            $"api/files/{file.Id}", new { name = file.Name, directoryId = directory.Id, content = "edited" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(file.Content, (await StoredFile(file.Id)).Content);
    }

    /// <summary>A member of one project moves a file into a directory of a project it holds nothing on.</summary>
    [Fact]
    public async Task Edit_moves_the_file_into_a_directory_of_another_project()
    {
        var (project, directory) = await SeedDirectory();
        var (_, foreignDirectory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        var file = await SeedFileLockedBy(directory, actor);

        var response = await Client(actor).PutAsJsonAsync(
            $"api/files/{file.Id}", new { name = file.Name, directoryId = foreignDirectory.Id, content = "moved" }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(foreignDirectory.Id, (await StoredFile(file.Id)).DirectoryId);
    }

    [Fact]
    public async Task PartialEdit_by_the_member_holding_the_lock_stores_only_the_content()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        var file = await SeedFileLockedBy(directory, actor);

        var response = await Client(actor).PatchAsJsonAsync($"api/files/{file.Id}", new { content = "patched" }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        var stored = await StoredFile(file.Id);
        Assert.Equal("patched", stored.Content);
        Assert.Equal(file.Name, stored.Name);
    }

    [Fact]
    public async Task PartialEdit_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();
        var file = await SeedFileLockedBy(directory, actor);

        var response = await Client(actor).PatchAsJsonAsync($"api/files/{file.Id}", new { content = "patched" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task PartialEdit_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();
        var file = await SeedFileLockedBy(directory, actor);

        var response = await Client(actor).PatchAsJsonAsync($"api/files/{file.Id}", new { content = "patched" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    /// <summary>A member of one project moves a file into a directory of a project it holds nothing on.</summary>
    [Fact]
    public async Task PartialEdit_moves_the_file_into_a_directory_of_another_project()
    {
        var (project, directory) = await SeedDirectory();
        var (_, foreignDirectory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        var file = await SeedFileLockedBy(directory, actor);

        var response = await Client(actor).PatchAsJsonAsync($"api/files/{file.Id}", new { directoryId = foreignDirectory.Id }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(foreignDirectory.Id, (await StoredFile(file.Id)).DirectoryId);
    }

    [Fact]
    public async Task Rename_by_a_member_holding_EditProject_stores_the_new_name_and_releases_the_lock()
    {
        var (project, _, file) = await SeedFile();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/files/{file.Id}/actions/rename", new { name = "renamed.tf" }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        var stored = await StoredFile(file.Id);
        Assert.Equal("renamed.tf", stored.Name);
        Assert.Null(stored.LockedById);
    }

    [Fact]
    public async Task Rename_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, _, file) = await SeedFile();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/files/{file.Id}/actions/rename", new { name = "renamed.tf" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(file.Name, (await StoredFile(file.Id)).Name);
    }

    [Fact]
    public async Task Rename_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, _, file) = await SeedFile();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/files/{file.Id}/actions/rename", new { name = "renamed.tf" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(file.Name, (await StoredFile(file.Id)).Name);
    }

    [Fact]
    public async Task Rename_by_a_caller_holding_EditProjects_stores_the_new_name()
    {
        var (_, _, file) = await SeedFile();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/files/{file.Id}/actions/rename", new { name = "renamed.tf" }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
    }

    // ---- DELETE api/files/{id} ------------------------------------------------------------------

    [Fact]
    public async Task Delete_by_a_member_holding_EditProject_marks_the_file_deleted()
    {
        var (project, _, file) = await SeedFile();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/files/{file.Id}", Ct);

        await AssertStatus(HttpStatusCode.NoContent, response);
        Assert.True((await StoredFile(file.Id)).IsDeleted);
    }

    [Fact]
    public async Task Delete_by_a_caller_holding_EditProjects_marks_the_file_deleted()
    {
        var (_, _, file) = await SeedFile();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.NoContent, await Client(actor).DeleteAsync($"api/files/{file.Id}", Ct));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, _, file) = await SeedFile();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/files/{file.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.False((await StoredFile(file.Id)).IsDeleted);
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, _, file) = await SeedFile();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/files/{file.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.False((await StoredFile(file.Id)).IsDeleted);
    }

    // ---- lock, unlock, force-unlock -------------------------------------------------------------

    [Fact]
    public async Task Lock_by_a_member_holding_EditProject_records_the_caller_as_the_holder()
    {
        var (project, _, file) = await SeedFile();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsync($"api/files/{file.Id}/actions/lock", null, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(actor.Id, (await StoredFile(file.Id)).LockedById);
    }

    [Fact]
    public async Task Lock_by_a_caller_holding_EditProjects_records_the_caller_as_the_holder()
    {
        var (_, _, file) = await SeedFile();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        await Client(actor).PostAsync($"api/files/{file.Id}/actions/lock", null, Ct);

        Assert.Equal(actor.Id, (await StoredFile(file.Id)).LockedById);
    }

    [Fact]
    public async Task Lock_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, _, file) = await SeedFile();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PostAsync($"api/files/{file.Id}/actions/lock", null, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Null((await StoredFile(file.Id)).LockedById);
    }

    [Fact]
    public async Task Lock_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, _, file) = await SeedFile();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).PostAsync($"api/files/{file.Id}/actions/lock", null, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Null((await StoredFile(file.Id)).LockedById);
    }

    [Fact]
    public async Task Lock_of_a_file_another_user_holds_is_a_conflict()
    {
        var (project, directory) = await SeedDirectory();
        var holder = await Actor().WithName("Holder").OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        var file = await SeedFileLockedBy(directory, holder);
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsync($"api/files/{file.Id}/actions/lock", null, Ct);

        var problem = await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.Equal(NotTheHolderTitle, problem.Title);
        Assert.Equal(holder.Id, (await StoredFile(file.Id)).LockedById);
    }

    [Fact]
    public async Task Unlock_by_the_holder_holding_EditProject_releases_the_lock()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        var file = await SeedFileLockedBy(directory, actor);

        var response = await Client(actor).PostAsync($"api/files/{file.Id}/actions/unlock", null, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Null((await StoredFile(file.Id)).LockedById);
    }

    [Fact]
    public async Task Unlock_is_forbidden_for_a_holder_holding_only_ViewProject()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();
        var file = await SeedFileLockedBy(directory, actor);

        var response = await Client(actor).PostAsync($"api/files/{file.Id}/actions/unlock", null, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(actor.Id, (await StoredFile(file.Id)).LockedById);
    }

    [Fact]
    public async Task Unlock_is_forbidden_for_a_holder_holding_EditProject_only_on_another_project()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();
        var file = await SeedFileLockedBy(directory, actor);

        var response = await Client(actor).PostAsync($"api/files/{file.Id}/actions/unlock", null, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task ForceUnlock_by_a_member_holding_ManageProject_releases_another_users_lock()
    {
        var (project, directory) = await SeedDirectory();
        var holder = await Actor().WithName("Holder").OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        var file = await SeedFileLockedBy(directory, holder);
        var actor = await Actor().OnProject(project, [ProjectPermission.ManageProject]).SeedAsync();

        var response = await Client(actor).PostAsync($"api/files/{file.Id}/actions/force-unlock", null, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Null((await StoredFile(file.Id)).LockedById);
    }

    [Fact]
    public async Task ForceUnlock_by_a_caller_holding_ManageProjects_releases_the_lock()
    {
        var (project, directory) = await SeedDirectory();
        var holder = await Actor().WithName("Holder").OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        var file = await SeedFileLockedBy(directory, holder);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageProjects).SeedAsync();

        await Client(actor).PostAsync($"api/files/{file.Id}/actions/force-unlock", null, Ct);

        Assert.Null((await StoredFile(file.Id)).LockedById);
    }

    [Fact]
    public async Task ForceUnlock_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var (project, directory) = await SeedDirectory();
        var holder = await Actor().WithName("Holder").OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        var file = await SeedFileLockedBy(directory, holder);
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsync($"api/files/{file.Id}/actions/force-unlock", null, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(holder.Id, (await StoredFile(file.Id)).LockedById);
    }

    [Fact]
    public async Task ForceUnlock_is_forbidden_for_a_caller_holding_ManageProject_only_on_another_project()
    {
        var (project, directory) = await SeedDirectory();
        var holder = await Actor().WithName("Holder").OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        var file = await SeedFileLockedBy(directory, holder);
        var actor = await Actor().OnNewProject(ProjectPermission.ManageProject).SeedAsync();

        var response = await Client(actor).PostAsync($"api/files/{file.Id}/actions/force-unlock", null, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(holder.Id, (await StoredFile(file.Id)).LockedById);
    }

    // ---- admin-lock, admin-unlock ---------------------------------------------------------------

    [Fact]
    public async Task AdminLock_by_a_member_holding_LockFiles_locks_the_file_administratively()
    {
        var (project, _, file) = await SeedFile();
        var actor = await Actor().OnProject(project, [ProjectPermission.LockFiles]).SeedAsync();

        var response = await Client(actor).PostAsync($"api/files/{file.Id}/actions/admin-lock", null, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.True((await StoredFile(file.Id)).AdministrativelyLocked);
    }

    [Fact]
    public async Task AdminLock_by_a_caller_holding_LockFiles_locks_the_file_administratively()
    {
        var (_, _, file) = await SeedFile();
        var actor = await Actor().WithSystemPermissions(SystemPermission.LockFiles).SeedAsync();

        await Client(actor).PostAsync($"api/files/{file.Id}/actions/admin-lock", null, Ct);

        Assert.True((await StoredFile(file.Id)).AdministrativelyLocked);
    }

    [Fact]
    public async Task AdminLock_is_forbidden_for_a_caller_holding_only_ManageProject()
    {
        var (project, _, file) = await SeedFile();
        var actor = await Actor().OnProject(project, [ProjectPermission.ManageProject]).SeedAsync();

        var response = await Client(actor).PostAsync($"api/files/{file.Id}/actions/admin-lock", null, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.False((await StoredFile(file.Id)).AdministrativelyLocked);
    }

    [Fact]
    public async Task AdminLock_is_forbidden_for_a_caller_holding_LockFiles_only_on_another_project()
    {
        var (_, _, file) = await SeedFile();
        var actor = await Actor().OnNewProject(ProjectPermission.LockFiles).SeedAsync();

        var response = await Client(actor).PostAsync($"api/files/{file.Id}/actions/admin-lock", null, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.False((await StoredFile(file.Id)).AdministrativelyLocked);
    }

    [Fact]
    public async Task AdminUnlock_by_a_member_holding_LockFiles_clears_the_administrative_lock()
    {
        var (project, _, file) = await SeedFile(administrativelyLocked: true);
        var actor = await Actor().OnProject(project, [ProjectPermission.LockFiles]).SeedAsync();

        var response = await Client(actor).PostAsync($"api/files/{file.Id}/actions/admin-unlock", null, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.False((await StoredFile(file.Id)).AdministrativelyLocked);
    }

    [Fact]
    public async Task AdminUnlock_is_forbidden_for_a_caller_holding_only_ManageProject()
    {
        var (project, _, file) = await SeedFile(administrativelyLocked: true);
        var actor = await Actor().OnProject(project, [ProjectPermission.ManageProject]).SeedAsync();

        var response = await Client(actor).PostAsync($"api/files/{file.Id}/actions/admin-unlock", null, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.True((await StoredFile(file.Id)).AdministrativelyLocked);
    }

    [Fact]
    public async Task AdminUnlock_is_forbidden_for_a_caller_holding_LockFiles_only_on_another_project()
    {
        var (_, _, file) = await SeedFile(administrativelyLocked: true);
        var actor = await Actor().OnNewProject(ProjectPermission.LockFiles).SeedAsync();

        var response = await Client(actor).PostAsync($"api/files/{file.Id}/actions/admin-unlock", null, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.True((await StoredFile(file.Id)).AdministrativelyLocked);
    }

    // ---- the administrative lock on writes (FileCommandHandler.CanLock) ----------------------------

    [Fact]
    public async Task Lock_of_an_administratively_locked_file_by_a_member_holding_EditProject_and_LockFiles_records_the_holder()
    {
        var (project, _, file) = await SeedFile(administrativelyLocked: true);
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject, ProjectPermission.LockFiles]).SeedAsync();

        var response = await Client(actor).PostAsync($"api/files/{file.Id}/actions/lock", null, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(actor.Id, (await StoredFile(file.Id)).LockedById);
    }

    [Fact]
    public async Task Lock_of_an_administratively_locked_file_without_LockFiles_is_a_conflict()
    {
        var (project, _, file) = await SeedFile(administrativelyLocked: true);
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsync($"api/files/{file.Id}/actions/lock", null, Ct);

        var problem = await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.Equal(AdminLockedTitle, problem.Title);
        Assert.Null((await StoredFile(file.Id)).LockedById);
    }

    [Fact]
    public async Task Lock_of_an_administratively_locked_file_is_a_conflict_for_a_member_holding_LockFiles_only_on_another_project()
    {
        var (project, _, file) = await SeedFile(administrativelyLocked: true);
        var actor = await Actor()
            .OnProject(project, [ProjectPermission.EditProject])
            .OnNewProject(ProjectPermission.LockFiles)
            .SeedAsync();

        var response = await Client(actor).PostAsync($"api/files/{file.Id}/actions/lock", null, Ct);

        var problem = await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.Equal(AdminLockedTitle, problem.Title);
        Assert.Null((await StoredFile(file.Id)).LockedById);
    }

    [Fact]
    public async Task Edit_of_an_administratively_locked_file_by_the_holder_holding_EditProject_and_LockFiles_stores_the_new_content()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject, ProjectPermission.LockFiles]).SeedAsync();
        var file = await SeedAdministrativelyLockedFileLockedBy(directory, actor);

        var response = await Client(actor).PutAsJsonAsync(
            $"api/files/{file.Id}", new { name = file.Name, directoryId = directory.Id, content = "edited" }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("edited", (await StoredFile(file.Id)).Content);
    }

    [Fact]
    public async Task Edit_of_an_administratively_locked_file_is_a_conflict_for_a_holder_holding_LockFiles_only_on_another_project()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor()
            .OnProject(project, [ProjectPermission.EditProject])
            .OnNewProject(ProjectPermission.LockFiles)
            .SeedAsync();
        var file = await SeedAdministrativelyLockedFileLockedBy(directory, actor);

        var response = await Client(actor).PutAsJsonAsync(
            $"api/files/{file.Id}", new { name = file.Name, directoryId = directory.Id, content = "edited" }, Ct);

        var problem = await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.Equal(AdminLockedTitle, problem.Title);
        Assert.Equal(file.Content, (await StoredFile(file.Id)).Content);
    }

    [Fact]
    public async Task PartialEdit_of_an_administratively_locked_file_by_the_holder_holding_EditProject_and_LockFiles_stores_the_content()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject, ProjectPermission.LockFiles]).SeedAsync();
        var file = await SeedAdministrativelyLockedFileLockedBy(directory, actor);

        var response = await Client(actor).PatchAsJsonAsync($"api/files/{file.Id}", new { content = "patched" }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("patched", (await StoredFile(file.Id)).Content);
    }

    [Fact]
    public async Task PartialEdit_of_an_administratively_locked_file_is_a_conflict_for_a_holder_holding_LockFiles_only_on_another_project()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor()
            .OnProject(project, [ProjectPermission.EditProject])
            .OnNewProject(ProjectPermission.LockFiles)
            .SeedAsync();
        var file = await SeedAdministrativelyLockedFileLockedBy(directory, actor);

        var response = await Client(actor).PatchAsJsonAsync($"api/files/{file.Id}", new { content = "patched" }, Ct);

        var problem = await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.Equal(AdminLockedTitle, problem.Title);
        Assert.Equal(file.Content, (await StoredFile(file.Id)).Content);
    }

    [Fact]
    public async Task Rename_of_an_administratively_locked_file_by_a_member_holding_EditProject_and_LockFiles_stores_the_new_name()
    {
        var (project, _, file) = await SeedFile(administrativelyLocked: true);
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject, ProjectPermission.LockFiles]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/files/{file.Id}/actions/rename", new { name = "renamed.tf" }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("renamed.tf", (await StoredFile(file.Id)).Name);
    }

    [Fact]
    public async Task Rename_of_an_administratively_locked_file_is_a_conflict_for_a_member_holding_LockFiles_only_on_another_project()
    {
        var (project, _, file) = await SeedFile(administrativelyLocked: true);
        var actor = await Actor()
            .OnProject(project, [ProjectPermission.EditProject])
            .OnNewProject(ProjectPermission.LockFiles)
            .SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/files/{file.Id}/actions/rename", new { name = "renamed.tf" }, Ct);

        var problem = await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.Equal(AdminLockedTitle, problem.Title);
        Assert.Equal(file.Name, (await StoredFile(file.Id)).Name);
    }

    [Fact]
    public async Task Delete_of_an_administratively_locked_file_by_a_member_holding_EditProject_and_LockFiles_marks_it_deleted()
    {
        var (project, _, file) = await SeedFile(administrativelyLocked: true);
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject, ProjectPermission.LockFiles]).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/files/{file.Id}", Ct);

        await AssertStatus(HttpStatusCode.NoContent, response);
        Assert.True((await StoredFile(file.Id)).IsDeleted);
    }

    [Fact]
    public async Task Delete_of_an_administratively_locked_file_is_a_conflict_for_a_member_holding_LockFiles_only_on_another_project()
    {
        var (project, _, file) = await SeedFile(administrativelyLocked: true);
        var actor = await Actor()
            .OnProject(project, [ProjectPermission.EditProject])
            .OnNewProject(ProjectPermission.LockFiles)
            .SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/files/{file.Id}", Ct);

        var problem = await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.Equal(AdminLockedTitle, problem.Title);
        Assert.False((await StoredFile(file.Id)).IsDeleted);
    }

    // ---- the edit lock another user holds (File.VerifyLock) ------------------------------------------

    [Fact]
    public async Task PartialEdit_of_a_file_another_user_holds_is_a_conflict()
    {
        var (project, directory) = await SeedDirectory();
        var holder = await Actor().WithName("Holder").OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        var file = await SeedFileLockedBy(directory, holder);
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PatchAsJsonAsync($"api/files/{file.Id}", new { content = "patched" }, Ct);

        var problem = await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.Equal(NotTheHolderTitle, problem.Title);
        Assert.Equal(file.Content, (await StoredFile(file.Id)).Content);
    }

    [Fact]
    public async Task Unlock_of_a_file_another_user_holds_is_a_conflict()
    {
        var (project, directory) = await SeedDirectory();
        var holder = await Actor().WithName("Holder").OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        var file = await SeedFileLockedBy(directory, holder);
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsync($"api/files/{file.Id}/actions/unlock", null, Ct);

        var problem = await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.Equal(NotTheHolderTitle, problem.Title);
        Assert.Equal(holder.Id, (await StoredFile(file.Id)).LockedById);
    }

    [Fact]
    public async Task Rename_of_a_file_another_user_holds_is_a_conflict()
    {
        var (project, directory) = await SeedDirectory();
        var holder = await Actor().WithName("Holder").OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        var file = await SeedFileLockedBy(directory, holder);
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/files/{file.Id}/actions/rename", new { name = "renamed.tf" }, Ct);

        var problem = await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.Equal(RenameWhileHeldTitle, problem.Title);
        Assert.Equal(file.Name, (await StoredFile(file.Id)).Name);
    }

    [Fact]
    public async Task Delete_of_a_file_another_user_holds_is_a_conflict()
    {
        var (project, directory) = await SeedDirectory();
        var holder = await Actor().WithName("Holder").OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        var file = await SeedFileLockedBy(directory, holder);
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/files/{file.Id}", Ct);

        var problem = await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.Equal(NotTheHolderTitle, problem.Title);
        Assert.False((await StoredFile(file.Id)).IsDeleted);
    }

    // ---- POST api/files/actions/tag -------------------------------------------------------------

    /// <summary>A member holding EditProject on the file's project is refused tagging it.</summary>
    // Same case as Tag_by_a_member_holding_only_ViewProject_stores_a_tagged_version.
    [Fact]
    public async Task Tag_is_forbidden_for_a_member_holding_EditProject_on_the_files_project()
    {
        var (project, _, file) = await SeedFile();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/files/actions/tag", new { tag = "v1", fileIds = new[] { file.Id } }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    /// <summary>A caller holding only ViewProject on the file's project tags it.</summary>
    [Fact]
    public async Task Tag_by_a_member_holding_only_ViewProject_stores_a_tagged_version()
    {
        var (project, _, file) = await SeedFile();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/files/actions/tag", new { tag = "v1", fileIds = new[] { file.Id } }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        await using var context = NewContext();
        Assert.True(await context.FileVersions.AnyAsync(x => x.FileId == file.Id && x.Tag == "v1" && x.TaggedById == actor.Id, Ct));
    }

    // ---- helpers --------------------------------------------------------------------------------

    private async Task<(Project Project, Directory Directory)> SeedDirectory()
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        await Seed(project, directory);

        return (project, directory);
    }

    private async Task<(Project Project, Directory Directory, File File)> SeedFile(bool administrativelyLocked = false)
    {
        var (project, directory) = await SeedDirectory();
        var file = TestData.File(directory);

        if (administrativelyLocked)
        {
            file.AdministrativelyLock(canLock: true);
        }

        await Seed(file);

        return (project, directory, file);
    }

    /// <summary>A file of <paramref name="directory"/> whose edit lock <paramref name="holder"/> holds.</summary>
    private async Task<File> SeedFileLockedBy(Directory directory, TestActor holder)
    {
        var file = TestData.File(directory);
        file.Lock(holder.Id, canLock: false);
        await Seed(file);

        return file;
    }

    /// <summary>
    /// A file of <paramref name="directory"/> whose edit lock <paramref name="holder"/> holds, and which an
    /// administrator has locked since.
    /// </summary>
    private async Task<File> SeedAdministrativelyLockedFileLockedBy(Directory directory, TestActor holder)
    {
        var file = TestData.File(directory);
        file.Lock(holder.Id, canLock: false);
        file.AdministrativelyLock(canLock: true);
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
}
