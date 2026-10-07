// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Directory = Caster.Api.Domain.Models.Directory;
using DirectoryView = Caster.Api.Features.Directories.Directory;

namespace Caster.Api.Tests.Features.Directories;

/// <summary>
/// <c>DirectoriesController</c>: reading a project's directories needs ViewProject on it (or ViewProjects),
/// changing them EditProject (or EditProjects), each checked against the project the directory is in.
/// </summary>
public class DirectoryRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    private const string ImportedFileName = "main.tf";

    /// <summary>Content no seeded file has, so a read-back proves the import wrote it.</summary>
    private const string ImportedContent = "# imported";

    // ---- GET api/directories/{id} ---------------------------------------------------------------

    [Fact]
    public async Task Get_returns_the_directory_to_a_member_holding_ViewProject()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/directories/{directory.Id}", Ct);

        Assert.Equal(directory.Name, (await ReadAsync<DirectoryView>(response)).Name);
    }

    [Fact]
    public async Task Get_returns_the_directory_to_a_caller_holding_ViewProjects()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).GetAsync($"api/directories/{directory.Id}", Ct));
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/directories/{directory.Id}", Ct));
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_ViewProject_only_on_another_project()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/directories/{directory.Id}", Ct));
    }

    [Fact]
    public async Task Get_with_includeRelated_returns_the_directorys_files_and_workspaces()
    {
        var (_, directory) = await SeedDirectory();
        var file = TestData.File(directory);
        var workspace = TestData.Workspace(directory);
        await Seed(file, workspace);

        var response = await RootClient.GetAsync($"api/directories/{directory.Id}?includeRelated=true", Ct);

        var view = await ReadAsync<DirectoryView>(response);
        Assert.Equal([file.Id], view.Files.Select(x => x.Id));
        Assert.Equal([workspace.Id], view.Workspaces.Select(x => x.Id));
    }

    // ---- GET api/projects/{projectId}/directories, GET api/directories/{id}/children ------------

    [Fact]
    public async Task GetByProject_lists_the_top_level_directories_for_a_member_holding_ViewProject()
    {
        var (project, directory) = await SeedDirectory();
        await Seed(TestData.Directory(project, "child", directory));
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/projects/{project.Id}/directories", Ct);

        Assert.Equal([directory.Id], (await ReadAsync<DirectoryView[]>(response)).Select(x => x.Id));
    }

    [Fact]
    public async Task GetByProject_lists_the_directories_for_a_caller_holding_ViewProjects()
    {
        var (project, _) = await SeedDirectory();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).GetAsync($"api/projects/{project.Id}/directories", Ct));
    }

    [Fact]
    public async Task GetByProject_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var (project, _) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/projects/{project.Id}/directories", Ct));
    }

    [Fact]
    public async Task GetByProject_is_forbidden_for_a_caller_holding_ViewProject_only_on_another_project()
    {
        var (project, _) = await SeedDirectory();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/projects/{project.Id}/directories", Ct));
    }

    [Fact]
    public async Task GetChildren_lists_the_descendants_for_a_member_holding_ViewProject()
    {
        var (project, directory) = await SeedDirectory();
        var child = TestData.Directory(project, "child", directory);
        var grandchild = TestData.Directory(project, "grandchild", child);
        await Seed(child, grandchild);
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/directories/{directory.Id}/children", Ct);

        Assert.Equal(new[] { child.Id, grandchild.Id }.Order(), (await ReadAsync<DirectoryView[]>(response)).Select(x => x.Id).Order());
    }

    [Fact]
    public async Task GetChildren_is_forbidden_for_a_caller_holding_ViewProject_only_on_another_project()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/directories/{directory.Id}/children", Ct));
    }

    [Fact]
    public async Task GetChildren_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/directories/{directory.Id}/children", Ct));
    }

    // ---- GET api/directories --------------------------------------------------------------------

    [Fact]
    public async Task GetAll_lists_every_directory_for_a_caller_holding_ViewProjects()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var response = await Client(actor).GetAsync("api/directories", Ct);

        Assert.Contains(directory.Id, (await ReadAsync<DirectoryView[]>(response)).Select(x => x.Id));
    }

    [Fact]
    public async Task GetAll_lists_only_the_directories_of_the_callers_projects()
    {
        var (mine, myDirectory) = await SeedDirectory();
        await SeedDirectory();
        var actor = await Actor().OnProject(mine, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync("api/directories", Ct);

        Assert.Equal([myDirectory.Id], (await ReadAsync<DirectoryView[]>(response)).Select(x => x.Id));
    }

    /// <summary>A membership whose role grants nothing still lists the project's directories.</summary>
    [Fact]
    public async Task GetAll_lists_a_projects_directories_to_a_member_whose_role_grants_no_permission()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, []).SeedAsync();

        var response = await Client(actor).GetAsync("api/directories", Ct);

        Assert.Equal([directory.Id], (await ReadAsync<DirectoryView[]>(response)).Select(x => x.Id));
    }

    /// <summary>A project membership held through a group lists none of the project's directories.</summary>
    [Fact]
    public async Task GetAll_lists_nothing_to_a_caller_whose_membership_comes_from_a_group()
    {
        var (project, _) = await SeedDirectory();
        // Same case as GetAll_lists_a_projects_directories_to_a_member_whose_role_grants_no_permission.
        var actor = await Actor().OnProjectThroughNewGroup(project, ProjectPermission.ViewProject).SeedAsync();

        var response = await Client(actor).GetAsync("api/directories", Ct);

        Assert.Empty(await ReadAsync<DirectoryView[]>(response));
    }

    // ---- POST api/directories -------------------------------------------------------------------

    [Fact]
    public async Task Create_by_a_member_holding_EditProject_stores_a_top_level_directory()
    {
        var project = await SeedProject();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/directories", new { name = "created", projectId = project.Id }, Ct);

        var created = await ReadAsync<DirectoryView>(response);
        var stored = await StoredDirectory(created.Id);
        Assert.Equal(project.Id, stored.ProjectId);
        Assert.Equal($"{created.Id}/", stored.Path);
    }

    [Fact]
    public async Task Create_by_a_caller_holding_EditProjects_stores_a_child_directory_under_its_parent()
    {
        var (project, parent) = await SeedDirectory();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(
            "api/directories", new { name = "child", projectId = project.Id, parentId = parent.Id }, Ct);

        var created = await ReadAsync<DirectoryView>(response);
        Assert.Equal($"{parent.Id}/{created.Id}/", (await StoredDirectory(created.Id)).Path);
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var project = await SeedProject();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/directories", new { name = "refused", projectId = project.Id }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.False(await DirectoryNamed("refused"));
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var project = await SeedProject();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/directories", new { name = "refused", projectId = project.Id }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.False(await DirectoryNamed("refused"));
    }

    [Fact]
    public async Task Create_under_a_parent_in_another_project_is_a_conflict()
    {
        var project = await SeedProject();
        var (_, foreignParent) = await SeedDirectory();

        var response = await RootClient.PostAsJsonAsync(
            "api/directories", new { name = "child", projectId = project.Id, parentId = foreignParent.Id }, Ct);

        await AssertProblem(HttpStatusCode.Conflict, response);
    }

    [Fact]
    public async Task Create_in_an_unknown_project_is_a_bad_request()
    {
        var response = await RootClient.PostAsJsonAsync("api/directories", new { name = "orphan", projectId = Guid.NewGuid() }, Ct);

        await AssertProblem(HttpStatusCode.BadRequest, response);
    }

    // ---- PUT/PATCH api/directories/{id} ---------------------------------------------------------

    [Fact]
    public async Task Edit_by_a_member_holding_EditProject_stores_the_new_name()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/directories/{directory.Id}", new { name = "renamed" }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("renamed", (await StoredDirectory(directory.Id)).Name);
    }

    [Fact]
    public async Task Edit_by_a_caller_holding_EditProjects_stores_the_new_name()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/directories/{directory.Id}", new { name = "renamed" }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("renamed", (await StoredDirectory(directory.Id)).Name);
    }

    [Fact]
    public async Task Edit_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/directories/{directory.Id}", new { name = "renamed" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(directory.Name, (await StoredDirectory(directory.Id)).Name);
    }

    [Fact]
    public async Task Edit_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/directories/{directory.Id}", new { name = "renamed" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(directory.Name, (await StoredDirectory(directory.Id)).Name);
    }

    [Fact]
    public async Task Edit_to_a_new_parent_moves_the_descendants_paths_with_it()
    {
        var (project, directory) = await SeedDirectory();
        var child = TestData.Directory(project, "child", directory);
        var newParent = TestData.Directory(project, "new-parent");
        await Seed(child, newParent);

        await RootClient.PutAsJsonAsync($"api/directories/{directory.Id}", new { name = directory.Name, parentId = newParent.Id }, Ct);

        Assert.Equal($"{newParent.Id}/{directory.Id}/{child.Id}/", (await StoredDirectory(child.Id)).Path);
    }

    /// <summary>A member of one project moves its directory under a directory of a project it holds nothing on.</summary>
    [Fact]
    public async Task Edit_moves_a_directory_under_a_parent_in_another_project()
    {
        var (project, directory) = await SeedDirectory();
        var (_, foreignParent) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync(
            $"api/directories/{directory.Id}", new { name = directory.Name, parentId = foreignParent.Id }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal($"{foreignParent.Id}/{directory.Id}/", (await StoredDirectory(directory.Id)).Path);
    }

    [Fact]
    public async Task PartialEdit_by_a_member_holding_EditProject_stores_only_the_name()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PatchAsJsonAsync($"api/directories/{directory.Id}", new { name = "patched" }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        var stored = await StoredDirectory(directory.Id);
        Assert.Equal("patched", stored.Name);
        Assert.Null(stored.ParentId);
    }

    [Fact]
    public async Task PartialEdit_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PatchAsJsonAsync($"api/directories/{directory.Id}", new { name = "patched" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(directory.Name, (await StoredDirectory(directory.Id)).Name);
    }

    [Fact]
    public async Task PartialEdit_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).PatchAsJsonAsync($"api/directories/{directory.Id}", new { name = "patched" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task PartialEdit_by_a_caller_holding_EditProjects_stores_the_name()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Client(actor).PatchAsJsonAsync($"api/directories/{directory.Id}", new { name = "patched" }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
    }

    /// <summary>A member of one project moves its directory under a directory of a project it holds nothing on.</summary>
    [Fact]
    public async Task PartialEdit_moves_a_directory_under_a_parent_in_another_project()
    {
        // Same case as Edit_moves_a_directory_under_a_parent_in_another_project.
        var (project, directory) = await SeedDirectory();
        var (_, foreignParent) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PatchAsJsonAsync($"api/directories/{directory.Id}", new { parentId = foreignParent.Id }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal($"{foreignParent.Id}/{directory.Id}/", (await StoredDirectory(directory.Id)).Path);
    }

    // ---- DELETE api/directories/{id} ------------------------------------------------------------

    [Fact]
    public async Task Delete_by_a_member_holding_EditProject_removes_the_directory_and_its_children()
    {
        var (project, directory) = await SeedDirectory();
        var child = TestData.Directory(project, "child", directory);
        await Seed(child);
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/directories/{directory.Id}", Ct);

        await AssertStatus(HttpStatusCode.NoContent, response);
        Assert.Null(await StoredDirectory(directory.Id));
        Assert.Null(await StoredDirectory(child.Id));
    }

    [Fact]
    public async Task Delete_by_a_caller_holding_EditProjects_removes_the_directory()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.NoContent, await Client(actor).DeleteAsync($"api/directories/{directory.Id}", Ct));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/directories/{directory.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.NotNull(await StoredDirectory(directory.Id));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/directories/{directory.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.NotNull(await StoredDirectory(directory.Id));
    }

    [Fact]
    public async Task Delete_of_a_directory_whose_child_has_a_queued_run_is_a_conflict()
    {
        var (project, directory) = await SeedDirectory();
        var child = TestData.Directory(project, "child", directory);
        var workspace = TestData.Workspace(child);
        await Seed(child, workspace, TestData.Run(workspace));

        var response = await RootClient.DeleteAsync($"api/directories/{directory.Id}", Ct);

        await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.NotNull(await StoredDirectory(directory.Id));
    }

    // ---- export and import ----------------------------------------------------------------------

    [Fact]
    public async Task Export_returns_a_zip_to_a_member_holding_ViewProject()
    {
        var (project, directory) = await SeedDirectory();
        await Seed(TestData.File(directory));
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/directories/{directory.Id}/actions/export?archiveType=zip", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Export_is_forbidden_for_a_caller_holding_ViewProject_only_on_another_project()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        var response = await Client(actor).GetAsync($"api/directories/{directory.Id}/actions/export?archiveType=zip", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Export_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/directories/{directory.Id}/actions/export?archiveType=zip", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Import_by_a_member_holding_EditProject_replaces_the_content_of_a_file_of_the_same_name()
    {
        var (project, directory) = await SeedDirectory();
        var file = TestData.File(directory, ImportedFileName);
        await Seed(file);
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsync(ImportUrl(directory), ArchiveHelper.Zip(ImportedFileName, ImportedContent), Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(ImportedContent, await StoredContent(directory.Id, ImportedFileName));
    }

    [Fact]
    public async Task Import_by_a_caller_holding_EditProjects_adds_a_new_file()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Client(actor).PostAsync(ImportUrl(directory), ArchiveHelper.Zip(ImportedFileName, ImportedContent), Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(ImportedContent, await StoredContent(directory.Id, ImportedFileName));
    }

    [Fact]
    public async Task Import_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PostAsync(ImportUrl(directory), ArchiveHelper.Zip(ImportedFileName, ImportedContent), Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Import_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).PostAsync(ImportUrl(directory), ArchiveHelper.Zip(ImportedFileName, ImportedContent), Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    /// <summary>A member holding only ImportProject is refused an import.</summary>
    [Fact]
    public async Task Import_is_forbidden_for_a_caller_holding_only_ImportProject()
    {
        // Same case as ProjectRequestTests.Import_is_forbidden_for_a_caller_holding_only_ImportProject.
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.ImportProject]).SeedAsync();

        var response = await Client(actor).PostAsync(ImportUrl(directory), ArchiveHelper.Zip(ImportedFileName, ImportedContent), Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static string ImportUrl(Directory directory) => $"api/directories/{directory.Id}/actions/import";

    private async Task<string> StoredContent(Guid directoryId, string name)
    {
        await using var context = NewContext();

        return (await context.Files.SingleAsync(x => x.DirectoryId == directoryId && x.Name == name, Ct)).Content;
    }

    private async Task<Project> SeedProject()
    {
        var project = TestData.Project();
        await Seed(project);

        return project;
    }

    private async Task<(Project Project, Directory Directory)> SeedDirectory()
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        await Seed(project, directory);

        return (project, directory);
    }

    private async Task<Directory> StoredDirectory(Guid id)
    {
        await using var context = NewContext();

        return await context.Directories.SingleOrDefaultAsync(x => x.Id == id, Ct);
    }

    private async Task<bool> DirectoryNamed(string name)
    {
        await using var context = NewContext();

        return await context.Directories.AnyAsync(x => x.Name == name, Ct);
    }
}
