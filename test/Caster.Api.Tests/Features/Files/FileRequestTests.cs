// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
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

    /// <summary>The name of a file a refused or conflicting create would have stored.</summary>
    private const string InjectedName = "injected.tf";

    /// <summary>The title of the 409 <c>Create</c> answers for a workspace that is not in the file's directory.</summary>
    private const string WorkspaceOfAnotherDirectoryTitle = "File and Workspace must be in the same Directory";

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
    public async Task Get_is_forbidden_for_a_caller_holding_only_EditProjects()
    {
        var (_, _, file) = await SeedFile();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

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
    public async Task GetFileVersions_lists_the_versions_for_a_caller_holding_ViewProjects()
    {
        var (_, _, file) = await SeedFile();
        var version = TestData.FileVersion(file);
        await Seed(version);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var response = await Client(actor).GetAsync($"api/files/{file.Id}/versions", Ct);

        Assert.Equal([version.Id], (await ReadAsync<FileVersionView[]>(response)).Select(x => x.Id));
    }

    [Fact]
    public async Task GetFileVersions_is_forbidden_for_a_caller_holding_only_EditProjects()
    {
        var (_, _, file) = await SeedFile();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/files/{file.Id}/versions", Ct));
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

    [Fact]
    public async Task GetFileVersion_returns_the_version_with_its_content_to_a_member_holding_ViewProject()
    {
        var (project, _, file) = await SeedFile();
        var version = TestData.FileVersion(file);
        await Seed(version);
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/files/versions/{version.Id}", Ct);

        var stored = await ReadAsync<FileVersionView>(response);
        Assert.Equal(version.Id, stored.Id);
        Assert.Equal(file.Content, stored.Content);
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

    // ---- file history, deleted files included -------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetFileVersions_lists_the_saved_version_without_content_to_a_member_holding_ViewProject_including_a_deleted_file(bool deleted)
    {
        var (project, file, versionId) = await SeedSavedFile(deleted);
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/files/{file.Id}/versions", Ct);

        var version = Assert.Single(await ReadAsync<FileVersionView[]>(response));
        Assert.Equal(versionId, version.Id);
        Assert.Equal(file.Id, version.FileId);
        Assert.Null(version.Content);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetFileVersions_lists_the_saved_version_without_content_to_a_caller_holding_ViewProjects_including_a_deleted_file(bool deleted)
    {
        var (_, file, versionId) = await SeedSavedFile(deleted);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var response = await Client(actor).GetAsync($"api/files/{file.Id}/versions", Ct);

        var version = Assert.Single(await ReadAsync<FileVersionView[]>(response));
        Assert.Equal(versionId, version.Id);
        Assert.Equal(file.Id, version.FileId);
        Assert.Null(version.Content);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetFileVersion_returns_the_saved_version_with_its_content_to_a_member_holding_ViewProject_including_a_deleted_file(bool deleted)
    {
        var (project, file, versionId) = await SeedSavedFile(deleted);
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/files/versions/{versionId}", Ct);

        var version = await ReadAsync<FileVersionView>(response);
        Assert.Equal(file.Id, version.FileId);
        Assert.Equal(file.Content, version.Content);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetFileVersion_returns_the_saved_version_with_its_content_to_a_caller_holding_ViewProjects_including_a_deleted_file(bool deleted)
    {
        var (_, file, versionId) = await SeedSavedFile(deleted);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var response = await Client(actor).GetAsync($"api/files/versions/{versionId}", Ct);

        var version = await ReadAsync<FileVersionView>(response);
        Assert.Equal(file.Id, version.FileId);
        Assert.Equal(file.Content, version.Content);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetFileVersions_is_forbidden_for_a_member_holding_ViewProject_only_on_another_project_including_a_deleted_file(bool deleted)
    {
        var (_, file, _) = await SeedSavedFile(deleted);
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/files/{file.Id}/versions", Ct));
    }

    /// <summary>Every version of a file of another project is refused: a saved one, a tagged one, one of a deleted file.</summary>
    [Theory]
    [InlineData(false, null)]
    [InlineData(false, TwoProjectGraph.SharedTag)]
    [InlineData(true, null)]
    public async Task GetFileVersion_is_forbidden_for_a_member_holding_ViewProject_only_on_another_project_including_tagged_and_deleted_versions(bool deleted, string tag)
    {
        var (_, _, versionId) = await SeedSavedFile(deleted, tag);
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/files/versions/{versionId}", Ct));
    }

    [Fact]
    public async Task GetFileVersion_is_forbidden_for_a_member_holding_only_EditProject()
    {
        var (project, _, versionId) = await SeedSavedFile(deleted: false);
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/files/versions/{versionId}", Ct));
    }

    /// <summary>A version id that does not exist resolves to no project, so a member is refused before it is looked up.</summary>
    [Fact]
    public async Task GetFileVersion_of_an_unknown_version_is_forbidden_for_a_member_holding_ViewProject()
    {
        var (project, _, _) = await SeedSavedFile(deleted: false);
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/files/versions/{Guid.NewGuid()}", Ct));
    }

    [Fact]
    public async Task GetFileVersion_of_an_unknown_version_is_not_found_for_a_caller_holding_ViewProjects()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        await AssertProblem(HttpStatusCode.NotFound, await Client(actor).GetAsync($"api/files/versions/{Guid.NewGuid()}", Ct));
    }

    /// <summary>
    /// A version's project is the project of the version's own file: a version of project B's file whose id
    /// is also the id of a file of the caller's project A is refused.
    /// </summary>
    [Fact]
    public async Task GetFileVersion_is_forbidden_for_a_version_of_another_project_whose_id_is_a_file_of_the_callers_project()
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.ViewProject]).SeedAsync();
        await Seed(new FileVersion { Id = graph.FileA.Id, FileId = graph.FileB.Id, Name = "foreign.tf", Content = "foreign" });

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/files/versions/{graph.FileA.Id}", Ct));
    }

    [Fact]
    public async Task Get_returns_a_deleted_file_to_a_member_holding_ViewProject()
    {
        var (project, file, _) = await SeedSavedFile(deleted: true);
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/files/{file.Id}", Ct);

        Assert.Equal(file.Id, (await ReadAsync<FileView>(response)).Id);
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

    [Fact]
    public async Task GetByDirectory_is_forbidden_for_a_caller_holding_only_EditProjects()
    {
        var (_, directory, _) = await SeedFile();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/directories/{directory.Id}/files", Ct));
    }

    [Fact]
    public async Task GetByDirectory_lists_only_the_directorys_live_files_without_their_content()
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/directories/{graph.DirectoryA.Id}/files?includeDeleted=false&includeContent=false", Ct);

        var files = await ReadAsync<FileView[]>(response);
        Assert.Equal(new[] { graph.FileA.Id, graph.AnotherFileA.Id }.Order(), files.Select(x => x.Id).Order());
        Assert.All(files, file => Assert.Equal(graph.DirectoryA.Id, file.DirectoryId));
        Assert.All(files, file => Assert.Null(file.Content));
        await AssertGraphFilesUnchanged();
    }

    [Fact]
    public async Task GetByDirectory_with_includeContent_lists_only_the_directorys_live_files_with_their_content()
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/directories/{graph.DirectoryA.Id}/files?includeDeleted=false&includeContent=true", Ct);

        var files = await ReadAsync<FileView[]>(response);
        Assert.Equal(new[] { graph.FileA.Id, graph.AnotherFileA.Id }.Order(), files.Select(x => x.Id).Order());
        Assert.All(files, file => Assert.Equal(graph.DirectoryA.Id, file.DirectoryId));
        Assert.All(files, file => Assert.Equal(file.Name, file.Content));
        await AssertGraphFilesUnchanged();
    }

    [Fact]
    public async Task GetByDirectory_with_includeDeleted_lists_the_directorys_deleted_files_too_without_their_content()
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/directories/{graph.DirectoryA.Id}/files?includeDeleted=true&includeContent=false", Ct);

        var files = await ReadAsync<FileView[]>(response);
        Assert.Equal(new[] { graph.FileA.Id, graph.AnotherFileA.Id, graph.DeletedFileA.Id }.Order(), files.Select(x => x.Id).Order());
        Assert.All(files, file => Assert.Equal(graph.DirectoryA.Id, file.DirectoryId));
        Assert.All(files, file => Assert.Null(file.Content));
        await AssertGraphFilesUnchanged();
    }

    [Fact]
    public async Task GetByDirectory_with_includeDeleted_and_includeContent_lists_the_directorys_live_and_deleted_files_with_their_content()
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/directories/{graph.DirectoryA.Id}/files?includeDeleted=true&includeContent=true", Ct);

        var files = await ReadAsync<FileView[]>(response);
        Assert.Equal(new[] { graph.FileA.Id, graph.AnotherFileA.Id, graph.DeletedFileA.Id }.Order(), files.Select(x => x.Id).Order());
        Assert.All(files, file => Assert.Equal(graph.DirectoryA.Id, file.DirectoryId));
        Assert.All(files, file => Assert.Equal(file.Name, file.Content));
        await AssertGraphFilesUnchanged();
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
    public async Task Create_is_forbidden_for_a_caller_holding_only_ViewProjects()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

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

    // ---- POST api/files: the workspace must be in the file's directory ---------------------------

    [Fact]
    public async Task Create_with_a_workspace_of_another_directory_of_its_project_is_a_conflict_for_a_member_holding_EditProject()
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/files",
            new { name = InjectedName, content = "injected", directoryId = graph.DirectoryA.Id, workspaceId = graph.AlternateWorkspaceA.Id }, Ct);

        var problem = await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.Equal(WorkspaceOfAnotherDirectoryTitle, problem.Title);
        await AssertGraphFilesUnchanged();
        Assert.False(await FileNamed(InjectedName));
    }

    [Fact]
    public async Task Create_with_a_workspace_of_another_directory_of_its_project_is_a_conflict_for_a_caller_holding_EditProjects()
    {
        var graph = await SeedGraph();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/files",
            new { name = InjectedName, content = "injected", directoryId = graph.DirectoryA.Id, workspaceId = graph.AlternateWorkspaceA.Id }, Ct);

        var problem = await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.Equal(WorkspaceOfAnotherDirectoryTitle, problem.Title);
        await AssertGraphFilesUnchanged();
        Assert.False(await FileNamed(InjectedName));
    }

    [Fact]
    public async Task Create_with_a_workspace_of_another_project_is_a_conflict_for_a_member_holding_EditProject()
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/files",
            new { name = InjectedName, content = "injected", directoryId = graph.DirectoryA.Id, workspaceId = graph.WorkspaceB.Id }, Ct);

        var problem = await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.Equal(WorkspaceOfAnotherDirectoryTitle, problem.Title);
        await AssertGraphFilesUnchanged();
        Assert.False(await FileNamed(InjectedName));
    }

    [Fact]
    public async Task Create_with_a_workspace_of_another_project_is_a_conflict_for_a_caller_holding_EditProjects()
    {
        var graph = await SeedGraph();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/files",
            new { name = InjectedName, content = "injected", directoryId = graph.DirectoryA.Id, workspaceId = graph.WorkspaceB.Id }, Ct);

        var problem = await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.Equal(WorkspaceOfAnotherDirectoryTitle, problem.Title);
        await AssertGraphFilesUnchanged();
        Assert.False(await FileNamed(InjectedName));
    }

    [Fact]
    public async Task Create_with_the_workspace_of_its_directory_stores_the_file_in_that_workspace()
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/files",
            new { name = "new.tf", content = "new", directoryId = graph.DirectoryA.Id, workspaceId = graph.WorkspaceA.Id }, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        var created = await ReadAsync<FileView>(response);
        var stored = await StoredFile(created.Id);
        Assert.Equal(graph.DirectoryA.Id, stored.DirectoryId);
        Assert.Equal(graph.WorkspaceA.Id, stored.WorkspaceId);
        Assert.Equal("new", stored.Content);
        Assert.Single(await StoredVersions(created.Id));
    }

    [Fact]
    public async Task Create_without_a_workspace_stores_a_file_of_the_directory_alone()
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/files",
            new { name = "new.tf", content = "new", directoryId = graph.DirectoryA.Id, workspaceId = (Guid?)null }, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        var created = await ReadAsync<FileView>(response);
        var stored = await StoredFile(created.Id);
        Assert.Equal(graph.DirectoryA.Id, stored.DirectoryId);
        Assert.Null(stored.WorkspaceId);
        Assert.Equal("new", stored.Content);
        Assert.Single(await StoredVersions(created.Id));
    }

    /// <summary>The directory is authorized before the workspace the body names is looked up.</summary>
    [Fact]
    public async Task Create_naming_a_workspace_that_does_not_exist_is_forbidden_in_a_directory_of_another_project()
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/files",
            new { name = "new.tf", directoryId = graph.DirectoryB.Id, workspaceId = Guid.NewGuid() }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        await AssertGraphFilesUnchanged();
    }

    [Fact]
    public async Task Create_naming_a_workspace_that_does_not_exist_is_not_found()
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/files",
            new { name = "new.tf", directoryId = graph.DirectoryA.Id, workspaceId = Guid.NewGuid() }, Ct);

        await AssertProblem(HttpStatusCode.NotFound, response);
        await AssertGraphFilesUnchanged();
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
    public async Task Edit_is_forbidden_for_a_caller_holding_only_ViewProjects()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();
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

    /// <summary>An edit naming a directory of another project stores the content and leaves the file in its own directory.</summary>
    [Fact]
    public async Task Edit_keeps_the_file_in_its_directory_when_the_body_names_a_directory_of_another_project()
    {
        var (project, directory) = await SeedDirectory();
        var (_, foreignDirectory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        var file = await SeedFileLockedBy(directory, actor);

        var response = await Client(actor).PutAsJsonAsync(
            $"api/files/{file.Id}", new { name = file.Name, directoryId = foreignDirectory.Id, content = "moved" }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        var stored = await StoredFile(file.Id);
        Assert.Equal(directory.Id, stored.DirectoryId);
        Assert.Equal("moved", stored.Content);
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
    public async Task PartialEdit_by_the_holder_holding_EditProjects_stores_only_the_content()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();
        var file = await SeedFileLockedBy(directory, actor);

        var response = await Client(actor).PatchAsJsonAsync($"api/files/{file.Id}", new { content = "patched" }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        var stored = await StoredFile(file.Id);
        Assert.Equal("patched", stored.Content);
        Assert.Equal(file.Name, stored.Name);
    }

    [Fact]
    public async Task PartialEdit_is_forbidden_for_a_holder_holding_only_ViewProjects()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();
        var file = await SeedFileLockedBy(directory, actor);

        var response = await Client(actor).PatchAsJsonAsync($"api/files/{file.Id}", new { content = "patched" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(file.Content, (await StoredFile(file.Id)).Content);
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

    /// <summary>A partial edit naming a directory of another project leaves the file in its own directory.</summary>
    [Fact]
    public async Task PartialEdit_keeps_the_file_in_its_directory_when_the_body_names_a_directory_of_another_project()
    {
        var (project, directory) = await SeedDirectory();
        var (_, foreignDirectory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        var file = await SeedFileLockedBy(directory, actor);

        var response = await Client(actor).PatchAsJsonAsync($"api/files/{file.Id}", new { directoryId = foreignDirectory.Id }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(directory.Id, (await StoredFile(file.Id)).DirectoryId);
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
    public async Task Rename_is_forbidden_for_a_caller_holding_only_ViewProjects()
    {
        var (_, _, file) = await SeedFile();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

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

    // ---- PUT/PATCH api/files/{id}: the stored file decides ----------------------------------------

    /// <summary>
    /// A <c>directoryId</c> and <c>workspaceId</c> in the body, of another project's directory and workspace,
    /// of nothing, null or left out, are ignored: the file's name and content change and it stays where it
    /// is, one version is added, and the other project's workspace still holds only its own file.
    /// </summary>
    [Theory]
    [InlineData("PUT", "foreign")]
    [InlineData("PATCH", "foreign")]
    [InlineData("PUT", "missing")]
    [InlineData("PATCH", "missing")]
    [InlineData("PUT", "null")]
    [InlineData("PATCH", "null")]
    [InlineData("PUT", "omitted")]
    [InlineData("PATCH", "omitted")]
    public async Task Edit_and_PartialEdit_change_content_without_moving_ownership_or_injecting_into_a_foreign_workspace(string method, string parent)
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.EditProject]).SeedAsync();
        await LockFileFor(graph.FileA, actor);
        var body = new Dictionary<string, object> { ["name"] = "renamed.tf", ["content"] = "changed" };
        WithLegacyField(body, "directoryId", parent, graph.DirectoryB.Id);
        WithLegacyField(body, "workspaceId", parent, graph.WorkspaceB.Id);

        var response = await Send(actor, method, $"api/files/{graph.FileA.Id}", body);

        await AssertStatus(HttpStatusCode.OK, response);
        var stored = await StoredFile(graph.FileA.Id);
        Assert.Equal("renamed.tf", stored.Name);
        Assert.Equal("changed", stored.Content);
        Assert.Equal(graph.DirectoryA.Id, stored.DirectoryId);
        Assert.Equal(graph.WorkspaceA.Id, stored.WorkspaceId);
        Assert.Equal(TwoProjectGraph.VersionCount + 1, await ReadBack(db => db.FileVersions.CountAsync(Ct)));
        Assert.Equal(graph.FileB.Id, Assert.Single(await StoredWorkspaceFiles(graph.WorkspaceB.Id, graph.DirectoryB.Id)).Id);
    }

    /// <summary>The file on the route is authorized, not the file, directory or workspace the body names.</summary>
    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    public async Task Edit_and_PartialEdit_cannot_use_body_ownership_to_authorize_a_foreign_file(string method)
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Send(actor, method, $"api/files/{graph.FileB.Id}",
            new { id = graph.FileA.Id, name = InjectedName, content = "injected", directoryId = graph.DirectoryA.Id, workspaceId = graph.WorkspaceA.Id });

        await AssertProblem(HttpStatusCode.Forbidden, response);
        var stored = await StoredFile(graph.FileB.Id);
        Assert.Equal("b.tf", stored.Name);
        Assert.Equal("b.tf", stored.Content);
        Assert.Equal(graph.DirectoryB.Id, stored.DirectoryId);
        Assert.Equal(graph.WorkspaceB.Id, stored.WorkspaceId);
        Assert.Equal(TwoProjectGraph.VersionCount, await ReadBack(db => db.FileVersions.CountAsync(Ct)));
    }

    [Fact]
    public async Task Edit_of_a_deleted_file_by_a_member_holding_EditProject_is_not_found()
    {
        var (project, file, _) = await SeedSavedFile(deleted: true);
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/files/{file.Id}", new { name = "changed.tf", content = "changed" }, Ct);

        await AssertProblem(HttpStatusCode.NotFound, response);
        var stored = await StoredFile(file.Id);
        Assert.True(stored.IsDeleted);
        Assert.Equal(file.Content, stored.Content);
        Assert.Single(await StoredVersions(file.Id));
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
    public async Task Delete_is_forbidden_for_a_caller_holding_only_ViewProjects()
    {
        var (_, _, file) = await SeedFile();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

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
    public async Task Lock_is_forbidden_for_a_caller_holding_only_ViewProjects()
    {
        var (_, _, file) = await SeedFile();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

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
    public async Task Unlock_by_the_holder_holding_EditProjects_releases_the_lock()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();
        var file = await SeedFileLockedBy(directory, actor);

        var response = await Client(actor).PostAsync($"api/files/{file.Id}/actions/unlock", null, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Null((await StoredFile(file.Id)).LockedById);
    }

    [Fact]
    public async Task Unlock_is_forbidden_for_a_holder_holding_only_ViewProjects()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();
        var file = await SeedFileLockedBy(directory, actor);

        var response = await Client(actor).PostAsync($"api/files/{file.Id}/actions/unlock", null, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(actor.Id, (await StoredFile(file.Id)).LockedById);
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
    public async Task ForceUnlock_is_forbidden_for_a_caller_holding_only_EditProjects()
    {
        var (project, directory) = await SeedDirectory();
        var holder = await Actor().WithName("Holder").OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        var file = await SeedFileLockedBy(directory, holder);
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

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
    public async Task AdminLock_is_forbidden_for_a_caller_holding_only_ManageProjects()
    {
        var (_, _, file) = await SeedFile();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageProjects).SeedAsync();

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
    public async Task AdminUnlock_is_forbidden_for_a_caller_holding_only_ManageProjects()
    {
        var (_, _, file) = await SeedFile(administrativelyLocked: true);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageProjects).SeedAsync();

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
    public async Task Lock_of_an_administratively_locked_file_is_a_conflict_for_a_caller_holding_only_EditProjects()
    {
        // Gate: src/Caster.Api/Features/Files/Requests/Lock.cs#Lock.Handler.PerformOperation
        var (_, _, file) = await SeedFile(administrativelyLocked: true);
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

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
    public async Task Edit_of_an_administratively_locked_file_is_a_conflict_for_a_holder_holding_only_EditProjects()
    {
        // Gate: src/Caster.Api/Features/Files/Requests/Edit.cs#Edit.Handler.PerformOperation
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();
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
    public async Task Rename_of_an_administratively_locked_file_is_a_conflict_for_a_caller_holding_only_EditProjects()
    {
        // Gate: src/Caster.Api/Features/Files/Requests/Rename.cs#Rename.Handler.PerformOperation
        var (_, _, file) = await SeedFile(administrativelyLocked: true);
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

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

    [Fact]
    public async Task Delete_of_an_administratively_locked_file_is_a_conflict_for_a_caller_holding_only_EditProjects()
    {
        // Gate: src/Caster.Api/Features/Files/Requests/Delete.cs#Delete.Handler.PerformOperation
        var (_, _, file) = await SeedFile(administrativelyLocked: true);
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

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

    [Fact]
    public async Task Tag_by_a_member_holding_EditProject_on_the_files_project_stores_a_tagged_version()
    {
        var (project, _, file) = await SeedFile();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/files/actions/tag", new { tag = "v1", fileIds = new[] { file.Id } }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        await using var context = NewContext();
        Assert.True(await context.FileVersions.AnyAsync(x => x.FileId == file.Id && x.Tag == "v1" && x.TaggedById == actor.Id, Ct));
    }

    [Fact]
    public async Task Tag_by_a_member_holding_only_ViewProject_is_forbidden_and_stores_no_tagged_version()
    {
        // Gate: src/Caster.Api/Features/Files/Requests/Tag.cs#Tag.Handler.Authorize
        var (project, _, file) = await SeedFile();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/files/actions/tag", new { tag = "v1", fileIds = new[] { file.Id } }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        await using var context = NewContext();
        Assert.False(await context.FileVersions.AnyAsync(x => x.FileId == file.Id && x.Tag == "v1", Ct));
    }

    /// <summary>A member holding ViewProject on the file's project is refused tagging it, and no version is added.</summary>
    [Fact]
    public async Task Tag_is_forbidden_for_a_member_holding_only_ViewProject()
    {
        // Gate: src/Caster.Api/Features/Files/Requests/Tag.cs#Tag.Handler.Authorize
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/files/actions/tag", new { tag = TwoProjectGraph.SharedTag, fileIds = new[] { graph.FileA.Id } }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        await AssertGraphFilesUnchanged();
    }

    [Fact]
    public async Task Tag_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        // Gate: src/Caster.Api/Features/Files/Requests/Tag.cs#Tag.Handler.Authorize
        var graph = await SeedGraph();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/files/actions/tag", new { tag = TwoProjectGraph.SharedTag, fileIds = new[] { graph.FileA.Id } }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        await AssertGraphFilesUnchanged();
    }

    [Fact]
    public async Task Tag_is_forbidden_for_a_caller_holding_only_ViewProjects()
    {
        // Gate: src/Caster.Api/Features/Files/Requests/Tag.cs#Tag.Handler.Authorize
        var graph = await SeedGraph();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/files/actions/tag", new { tag = TwoProjectGraph.SharedTag, fileIds = new[] { graph.FileA.Id } }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        await AssertGraphFilesUnchanged();
    }

    /// <summary>Every file is authorized: one file of a project the caller holds nothing on refuses the whole batch.</summary>
    [Fact]
    public async Task Tag_is_forbidden_when_one_of_the_files_is_in_another_project()
    {
        // Gate: src/Caster.Api/Features/Files/Requests/Tag.cs#Tag.Handler.Authorize
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/files/actions/tag",
            new { tag = TwoProjectGraph.SharedTag, fileIds = new[] { graph.FileA.Id, graph.FileB.Id } }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        await AssertGraphFilesUnchanged();
    }

    /// <summary>A file id that does not exist resolves to no project, so a member is refused the batch.</summary>
    [Fact]
    public async Task Tag_is_forbidden_when_one_of_the_files_does_not_exist()
    {
        // Gate: src/Caster.Api/Features/Files/Requests/Tag.cs#Tag.Handler.Authorize
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/files/actions/tag",
            new { tag = TwoProjectGraph.SharedTag, fileIds = new[] { graph.FileA.Id, Guid.NewGuid() } }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        await AssertGraphFilesUnchanged();
    }

    [Fact]
    public async Task Tag_of_an_empty_list_is_forbidden()
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/files/actions/tag", new { tag = TwoProjectGraph.SharedTag, fileIds = Array.Empty<Guid>() }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        await AssertGraphFilesUnchanged();
    }

    [Fact]
    public async Task Tag_without_a_list_of_files_is_forbidden()
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/files/actions/tag", new { tag = TwoProjectGraph.SharedTag, fileIds = (Guid[])null }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        await AssertGraphFilesUnchanged();
    }

    /// <summary>
    /// A file named twice is tagged once, and the answer holds only the requested file's versions with the
    /// tag, not those of AnotherFileA, which carries the same tag.
    /// </summary>
    [Fact]
    public async Task Tag_by_a_member_holding_EditProject_tags_a_repeated_file_once_and_answers_only_its_versions()
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/files/actions/tag",
            new { tag = TwoProjectGraph.SharedTag, fileIds = new[] { graph.FileA.Id, graph.FileA.Id } }, Ct);

        var versions = await ReadAsync<FileVersionView[]>(response);
        Assert.Equal([graph.FileA.Id], versions.Select(x => x.FileId.Value).Distinct());
        Assert.All(versions, version => Assert.Null(version.Content));
        Assert.Equal(TwoProjectGraph.VersionCount + 1, await ReadBack(db => db.FileVersions.CountAsync(Ct)));
    }

    /// <summary>
    /// A caller holding EditProjects tags files of two projects; the answer holds the versions with the tag of
    /// those two files only (FileB's earlier one included), never AnotherFileA's.
    /// </summary>
    [Fact]
    public async Task Tag_by_a_caller_holding_EditProjects_tags_files_of_two_projects_and_answers_only_their_versions()
    {
        var graph = await SeedGraph();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/files/actions/tag",
            new { tag = TwoProjectGraph.SharedTag, fileIds = new[] { graph.FileA.Id, graph.FileB.Id } }, Ct);

        var versions = await ReadAsync<FileVersionView[]>(response);
        Assert.Equal(new[] { graph.FileA.Id, graph.FileB.Id }.Order(), versions.Select(x => x.FileId.Value).Distinct().Order());
        Assert.All(versions, version => Assert.Null(version.Content));
        Assert.Equal(TwoProjectGraph.VersionCount + 2, await ReadBack(db => db.FileVersions.CountAsync(Ct)));
    }

    /// <summary>Each file is authorized in turn on the request's one context, and each gets its tagged version.</summary>
    [Fact]
    public async Task Tag_by_a_member_holding_EditProject_tags_two_files_of_the_project()
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/files/actions/tag",
            new { tag = "batch", fileIds = new[] { graph.FileA.Id, graph.AnotherFileA.Id } }, Ct);

        Assert.Equal(2, (await ReadAsync<FileVersionView[]>(response)).Length);
        Assert.Equal(TwoProjectGraph.VersionCount + 2, await ReadBack(db => db.FileVersions.CountAsync(Ct)));
    }

    /// <summary>Every file is looked up before any is tagged, so a missing one leaves the others untagged.</summary>
    [Fact]
    public async Task Tag_by_a_caller_holding_EditProjects_of_a_file_that_does_not_exist_is_not_found()
    {
        var graph = await SeedGraph();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/files/actions/tag",
            new { tag = TwoProjectGraph.SharedTag, fileIds = new[] { graph.FileA.Id, Guid.NewGuid() } }, Ct);

        await AssertProblem(HttpStatusCode.NotFound, response);
        await AssertGraphFilesUnchanged();
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

    private Task<TwoProjectGraph> SeedGraph() => TwoProjectGraph.SeedAsync(Db, Root.Id, Ct);

    /// <summary>
    /// A file of a new project saved once by <see cref="ApiTestBase.Root"/>, deleted when asked, and tagged
    /// <paramref name="tag"/> when one is given; answers the id of its version carrying <paramref name="tag"/>
    /// (the saved one when none is given).
    /// </summary>
    private async Task<(Project Project, File File, Guid VersionId)> SeedSavedFile(bool deleted, string tag = null)
    {
        var (project, directory) = await SeedDirectory();
        var file = TestData.File(directory);
        file.Save(Root.Id, canLock: true, bypassLock: true);
        file.IsDeleted = deleted;
        await Seed(file);
        await TagStored(file, tag);

        var versionId = await ReadBack(db => db.FileVersions.Where(x => x.FileId == file.Id && x.Tag == tag).Select(x => x.Id).SingleAsync(Ct));

        return (project, file, versionId);
    }

    /// <summary>Tags the stored <paramref name="file"/> as Root, unless <paramref name="tag"/> is null.</summary>
    private async Task TagStored(File file, string tag)
    {
        if (tag is null)
        {
            return;
        }

        await using var context = NewContext();
        var stored = await context.Files.IgnoreQueryFilters().SingleAsync(x => x.Id == file.Id, Ct);
        stored.Tag(tag, Root.Id, TestData.DefaultDateCreated);
        await context.SaveChangesAsync(Ct);
    }

    /// <summary>Gives <paramref name="holder"/> the edit lock of the stored <paramref name="file"/>.</summary>
    private async Task LockFileFor(File file, TestActor holder)
    {
        await using var context = NewContext();
        var stored = await context.Files.SingleAsync(x => x.Id == file.Id, Ct);
        stored.Lock(holder.Id, canLock: false);
        await context.SaveChangesAsync(Ct);
    }

    /// <summary>The graph's files and versions are as it seeded them: none added, none removed.</summary>
    private async Task AssertGraphFilesUnchanged()
    {
        Assert.Equal(TwoProjectGraph.FileCount, await ReadBack(db => db.Files.IgnoreQueryFilters().CountAsync(Ct)));
        Assert.Equal(TwoProjectGraph.VersionCount, await ReadBack(db => db.FileVersions.CountAsync(Ct)));
    }

    private async Task<FileVersion[]> StoredVersions(Guid fileId)
    {
        await using var context = NewContext();

        return await context.FileVersions.Where(x => x.FileId == fileId).ToArrayAsync(Ct);
    }

    /// <summary>The files the stored workspace's runs would be given, as <c>CasterContext.GetWorkspaceFiles</c> gathers them.</summary>
    private async Task<List<File>> StoredWorkspaceFiles(Guid workspaceId, Guid directoryId)
    {
        await using var context = NewContext();
        var workspace = await context.Workspaces.SingleAsync(x => x.Id == workspaceId, Ct);
        var directory = await context.Directories.SingleAsync(x => x.Id == directoryId, Ct);

        return await context.GetWorkspaceFiles(workspace, directory);
    }

    private Task<HttpResponseMessage> Send(TestActor actor, string method, string url, object body) =>
        Client(actor).SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = JsonContent.Create(body) }, Ct);

    /// <summary>
    /// Sets <paramref name="field"/> of <paramref name="body"/> as an old client would send it: the id of a row
    /// of another project (<c>foreign</c>), an id nothing has (<c>missing</c>), <c>null</c>, or left out
    /// (<c>omitted</c>).
    /// </summary>
    private static void WithLegacyField(Dictionary<string, object> body, string field, string scenario, Guid foreignId)
    {
        if (scenario != "omitted")
        {
            body[field] = scenario switch { "foreign" => foreignId, "missing" => Guid.NewGuid(), _ => null };
        }
    }
}
