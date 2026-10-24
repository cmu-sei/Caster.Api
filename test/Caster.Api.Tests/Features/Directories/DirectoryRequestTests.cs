// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Directory = Caster.Api.Domain.Models.Directory;
using DirectoryView = Caster.Api.Features.Directories.Directory;
using File = Caster.Api.Domain.Models.File;
using ImportResultView = Caster.Api.Features.Directories.Import.ImportDirectoryResult;

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

    /// <summary>The directory the settings archives are exported from.</summary>
    private const string SourceDirectoryName = "infrastructure";

    /// <summary>Every directory with its files and their content.</summary>
    private const string ListingWithFiles = "api/directories?includeRelated=true&includeFileContent=true";

    /// <summary>The title of the 409 a move under a parent of another project answers.</summary>
    private const string ParentInAnotherProjectTitle = "Parent and child Directories must be in the same Project";

    /// <summary>The title of the 409 a move under the directory itself, or under one of its descendants, answers.</summary>
    private const string ParentBelowItselfTitle = "A Directory cannot be its own parent or a child of its descendants";

    /// <summary>A Terraform version the host has: <c>TestConfiguration</c> gives <c>Terraform:BinaryPath</c> a folder for it.</summary>
    private const string InstalledVersion = "1.5.7";

    /// <summary>A well-formed Terraform version <c>Terraform:BinaryPath</c> has no folder for.</summary>
    private const string NotInstalledVersion = "1.9.9";

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
    public async Task Get_is_forbidden_for_a_caller_holding_only_EditProjects()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

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
    public async Task GetByProject_is_forbidden_for_a_caller_holding_only_EditProjects()
    {
        var (project, _) = await SeedDirectory();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

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
    public async Task GetChildren_lists_the_descendants_for_a_caller_holding_ViewProjects()
    {
        var (project, directory) = await SeedDirectory();
        var child = TestData.Directory(project, "child", directory);
        var grandchild = TestData.Directory(project, "grandchild", child);
        await Seed(child, grandchild);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var response = await Client(actor).GetAsync($"api/directories/{directory.Id}/children", Ct);

        Assert.Equal(new[] { child.Id, grandchild.Id }.Order(), (await ReadAsync<DirectoryView[]>(response)).Select(x => x.Id).Order());
    }

    [Fact]
    public async Task GetChildren_is_forbidden_for_a_caller_holding_only_EditProjects()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Client(actor).GetAsync($"api/directories/{directory.Id}/children", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
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

    /// <summary>A system permission other than ViewProjects lists no project's directories without a membership.</summary>
    [Fact]
    public async Task GetAll_leaves_out_every_directory_for_a_caller_holding_only_EditProjects()
    {
        await SeedDirectory();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Client(actor).GetAsync("api/directories", Ct);

        Assert.Empty(await ReadAsync<DirectoryView[]>(response));
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

    [Fact]
    public async Task GetAll_lists_nothing_to_a_member_whose_role_grants_no_permission()
    {
        await SeedDirectory();
        var (project, _) = await SeedDirectory();
        var actor = await Actor().OnProject(project, []).SeedAsync();

        var response = await Client(actor).GetAsync("api/directories", Ct);

        Assert.Empty(await ReadAsync<DirectoryView[]>(response));
    }

    /// <summary>A membership held through a group lists that project's directories, and no other project's.</summary>
    [Fact]
    public async Task GetAll_lists_a_projects_directories_to_a_caller_whose_membership_comes_from_a_group()
    {
        var (project, directory) = await SeedDirectory();
        await SeedDirectory();
        var actor = await Actor().OnProjectThroughNewGroup(project, ProjectPermission.ViewProject).SeedAsync();

        var response = await Client(actor).GetAsync("api/directories", Ct);

        Assert.Equal([directory.Id], (await ReadAsync<DirectoryView[]>(response)).Select(x => x.Id));
    }

    /// <summary>A direct membership whose role grants ViewProject lists that project's directories, with their files and content, and not those of a project whose role grants nothing.</summary>
    [Fact]
    public async Task GetAll_lists_a_projects_directories_and_their_files_to_a_member_whose_role_grants_ViewProject()
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.ViewProject]).OnProject(graph.ProjectB, []).SeedAsync();

        var response = await Client(actor).GetAsync(ListingWithFiles, Ct);

        var directories = await ReadAsync<DirectoryView[]>(response);
        Assert.Equal(DirectoriesOf(graph).Order(), directories.Select(x => x.Id).Order());
        Assert.All(directories, x => Assert.Equal(graph.ProjectA.Id, x.ProjectId));
        var files = directories.SelectMany(x => x.Files).ToArray();
        Assert.Equal(new[] { graph.FileA.Id, graph.AnotherFileA.Id }.Order(), files.Select(x => x.Id).Order());
        Assert.All(files, x => Assert.NotNull(x.Content));
    }

    /// <summary>A direct membership whose role grants EditProject but not ViewProject lists nothing.</summary>
    [Fact]
    public async Task GetAll_lists_nothing_to_a_member_whose_role_grants_only_EditProject()
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.EditProject]).OnProject(graph.ProjectB, []).SeedAsync();

        var response = await Client(actor).GetAsync(ListingWithFiles, Ct);

        Assert.Empty(await ReadAsync<DirectoryView[]>(response));
    }

    /// <summary>A direct membership whose role grants nothing lists nothing.</summary>
    [Fact]
    public async Task GetAll_lists_nothing_to_a_member_whose_role_grants_nothing()
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, []).OnProject(graph.ProjectB, []).SeedAsync();

        var response = await Client(actor).GetAsync(ListingWithFiles, Ct);

        Assert.Empty(await ReadAsync<DirectoryView[]>(response));
    }

    /// <summary>A direct membership whose role grants every permission (the seeded Manager role) lists that project's directories.</summary>
    [Fact]
    public async Task GetAll_lists_a_projects_directories_and_their_files_to_a_member_whose_role_grants_every_permission()
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, roleId: TestData.ProjectRoles.Manager).OnProject(graph.ProjectB, []).SeedAsync();

        var response = await Client(actor).GetAsync(ListingWithFiles, Ct);

        var directories = await ReadAsync<DirectoryView[]>(response);
        Assert.Equal(DirectoriesOf(graph).Order(), directories.Select(x => x.Id).Order());
        Assert.All(directories, x => Assert.Equal(graph.ProjectA.Id, x.ProjectId));
        var files = directories.SelectMany(x => x.Files).ToArray();
        Assert.Equal(new[] { graph.FileA.Id, graph.AnotherFileA.Id }.Order(), files.Select(x => x.Id).Order());
        Assert.All(files, x => Assert.NotNull(x.Content));
    }

    /// <summary>A membership held through a group, whose role grants ViewProject, lists that project's directories.</summary>
    [Fact]
    public async Task GetAll_lists_a_projects_directories_and_their_files_to_a_member_of_a_group_whose_role_grants_ViewProject()
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProjectThroughNewGroup(graph.ProjectA, ProjectPermission.ViewProject).OnProject(graph.ProjectB, []).SeedAsync();

        var response = await Client(actor).GetAsync(ListingWithFiles, Ct);

        var directories = await ReadAsync<DirectoryView[]>(response);
        Assert.Equal(DirectoriesOf(graph).Order(), directories.Select(x => x.Id).Order());
        Assert.All(directories, x => Assert.Equal(graph.ProjectA.Id, x.ProjectId));
        var files = directories.SelectMany(x => x.Files).ToArray();
        Assert.Equal(new[] { graph.FileA.Id, graph.AnotherFileA.Id }.Order(), files.Select(x => x.Id).Order());
        Assert.All(files, x => Assert.NotNull(x.Content));
    }

    /// <summary>A membership held through a group, whose role grants EditProject but not ViewProject, lists nothing.</summary>
    [Fact]
    public async Task GetAll_lists_nothing_to_a_member_of_a_group_whose_role_grants_only_EditProject()
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProjectThroughNewGroup(graph.ProjectA, ProjectPermission.EditProject).OnProject(graph.ProjectB, []).SeedAsync();

        var response = await Client(actor).GetAsync(ListingWithFiles, Ct);

        Assert.Empty(await ReadAsync<DirectoryView[]>(response));
    }

    /// <summary>A membership held through a group, whose role grants nothing, lists nothing.</summary>
    [Fact]
    public async Task GetAll_lists_nothing_to_a_member_of_a_group_whose_role_grants_nothing()
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProjectThroughNewGroup(graph.ProjectA, []).OnProject(graph.ProjectB, []).SeedAsync();

        var response = await Client(actor).GetAsync(ListingWithFiles, Ct);

        Assert.Empty(await ReadAsync<DirectoryView[]>(response));
    }

    /// <summary>A membership held through a group, whose role grants every permission (the seeded Manager role), lists that project's directories.</summary>
    [Fact]
    public async Task GetAll_lists_a_projects_directories_and_their_files_to_a_member_of_a_group_whose_role_grants_every_permission()
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProjectThroughNewGroup(graph.ProjectA, TestData.ProjectRoles.Manager).OnProject(graph.ProjectB, []).SeedAsync();

        var response = await Client(actor).GetAsync(ListingWithFiles, Ct);

        var directories = await ReadAsync<DirectoryView[]>(response);
        Assert.Equal(DirectoriesOf(graph).Order(), directories.Select(x => x.Id).Order());
        Assert.All(directories, x => Assert.Equal(graph.ProjectA.Id, x.ProjectId));
        var files = directories.SelectMany(x => x.Files).ToArray();
        Assert.Equal(new[] { graph.FileA.Id, graph.AnotherFileA.Id }.Order(), files.Select(x => x.Id).Order());
        Assert.All(files, x => Assert.NotNull(x.Content));
    }

    [Fact]
    public async Task GetAll_lists_nothing_to_a_caller_on_no_project()
    {
        await SeedGraph();
        var actor = await Actor().SeedAsync();

        var response = await Client(actor).GetAsync("api/directories", Ct);

        Assert.Empty(await ReadAsync<DirectoryView[]>(response));
    }

    /// <summary>The seeded Observer role's system ViewProjects lists every project's directories without a membership.</summary>
    [Fact]
    public async Task GetAll_lists_every_projects_directories_to_a_caller_holding_the_Observer_role()
    {
        var graph = await SeedGraph();
        var actor = await Actor().WithRole(TestData.Roles.Observer).SeedAsync();

        var response = await Client(actor).GetAsync("api/directories", Ct);

        var directories = await ReadAsync<DirectoryView[]>(response);
        Assert.Equal(DirectoriesOf(graph).Append(graph.DirectoryB.Id).Order(), directories.Select(x => x.Id).Order());
        Assert.Contains(directories, x => x.ProjectId == graph.ProjectB.Id);
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
    public async Task Create_answers_an_id_that_is_already_taken_with_a_server_error()
    {
        var (project, directory) = await SeedDirectory();

        var response = await RootClient.PostAsJsonAsync(
            "api/directories", new { id = directory.Id, projectId = project.Id, name = "Again" }, Ct);

        var problem = await AssertProblem(HttpStatusCode.InternalServerError, response);
        Assert.Equal("An error occurred while saving the entity changes. See the inner exception for details.", problem.Detail);
        await using var context = NewContext();
        var stored = await context.Directories.SingleAsync(x => x.Id == directory.Id, Ct);
        Assert.Equal(directory.Name, stored.Name);
        Assert.Equal(directory.ProjectId, stored.ProjectId);
        Assert.Equal(directory.ParentId, stored.ParentId);
        Assert.Equal(directory.Path, stored.Path);
        Assert.False(await context.Directories.AnyAsync(x => x.Name == "Again", Ct));
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
    public async Task Create_is_forbidden_for_a_caller_holding_only_ViewProjects()
    {
        var project = await SeedProject();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

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

    /// <summary>A directory's name becomes a path segment of every archive it is exported in, where this one is reserved.</summary>
    [Fact]
    public async Task Create_answers_a_bad_request_for_a_name_an_archive_reserves()
    {
        var project = await SeedProject();

        var response = await RootClient.PostAsJsonAsync("api/directories", new { name = "__Workspaces__", projectId = project.Id }, Ct);

        await AssertProblem(HttpStatusCode.BadRequest, response);
        Assert.False(await DirectoryNamed("__Workspaces__"));
    }

    [Fact]
    public async Task Edit_answers_a_bad_request_for_a_name_that_climbs_out_of_its_parent()
    {
        var (_, directory) = await SeedDirectory();

        var response = await RootClient.PutAsJsonAsync($"api/directories/{directory.Id}", new { name = ".." }, Ct);

        await AssertProblem(HttpStatusCode.BadRequest, response);
        Assert.Equal(directory.Name, (await StoredDirectory(directory.Id)).Name);
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
    public async Task Edit_is_forbidden_for_a_caller_holding_only_ViewProjects()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

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

    [Fact]
    public async Task Edit_under_a_parent_in_another_project_is_a_conflict()
    {
        var (project, directory) = await SeedDirectory();
        var (_, foreignParent) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync(
            $"api/directories/{directory.Id}", new { name = directory.Name, parentId = foreignParent.Id }, Ct);

        var problem = await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.Equal(ParentInAnotherProjectTitle, problem.Title);
        Assert.Equal(directory.Path, (await StoredDirectory(directory.Id)).Path);
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
    public async Task PartialEdit_is_forbidden_for_a_caller_holding_only_ViewProjects()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

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

    [Fact]
    public async Task PartialEdit_under_a_parent_in_another_project_is_a_conflict()
    {
        var (project, directory) = await SeedDirectory();
        var (_, foreignParent) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PatchAsJsonAsync($"api/directories/{directory.Id}", new { parentId = foreignParent.Id }, Ct);

        var problem = await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.Equal(ParentInAnotherProjectTitle, problem.Title);
        Assert.Equal(directory.Path, (await StoredDirectory(directory.Id)).Path);
    }

    // ---- PUT/PATCH api/directories/{id}: a parent of the same project, never itself or below ------

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    public async Task Edit_and_PartialEdit_reject_a_parent_in_another_project_before_changing_any_path_for_a_member_holding_EditProject(string method)
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Send(actor, method, $"api/directories/{graph.ChildA.Id}", new { name = "changed", parentId = graph.DirectoryB.Id });

        var problem = await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.Equal(ParentInAnotherProjectTitle, problem.Title);
        await AssertChildUnchanged(graph);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    public async Task Edit_and_PartialEdit_reject_a_parent_in_another_project_before_changing_any_path_for_a_caller_holding_EditProjects(string method)
    {
        var graph = await SeedGraph();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Send(actor, method, $"api/directories/{graph.ChildA.Id}", new { name = "changed", parentId = graph.DirectoryB.Id });

        var problem = await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.Equal(ParentInAnotherProjectTitle, problem.Title);
        await AssertChildUnchanged(graph);
    }

    /// <summary>The directory on the route is authorized, not the directory or parent the body names.</summary>
    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    public async Task Edit_and_PartialEdit_cannot_authorize_a_directory_of_another_project_with_a_body_parent(string method)
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Send(actor, method, $"api/directories/{graph.DirectoryB.Id}",
            new { id = graph.ChildA.Id, name = "changed", parentId = graph.DirectoryA.Id });

        await AssertProblem(HttpStatusCode.Forbidden, response);
        var stored = await StoredDirectory(graph.DirectoryB.Id);
        Assert.Null(stored.ParentId);
        Assert.Equal(graph.ProjectB.Id, stored.ProjectId);
        Assert.Equal(graph.DirectoryB.Path, stored.Path);
        Assert.Equal("B", stored.Name);
        await AssertChildUnchanged(graph);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    public async Task Edit_and_PartialEdit_reject_the_directory_itself_as_its_parent(string method)
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Send(actor, method, $"api/directories/{graph.ChildA.Id}", new { name = "changed", parentId = graph.ChildA.Id });

        var problem = await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.Equal(ParentBelowItselfTitle, problem.Title);
        await AssertChildUnchanged(graph);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    public async Task Edit_and_PartialEdit_reject_a_descendant_as_the_parent(string method)
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Send(actor, method, $"api/directories/{graph.ChildA.Id}", new { name = "changed", parentId = graph.GrandchildA.Id });

        var problem = await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.Equal(ParentBelowItselfTitle, problem.Title);
        await AssertChildUnchanged(graph);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    public async Task Edit_and_PartialEdit_move_the_directory_within_its_project_and_its_descendants_paths_with_it(string method)
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Send(actor, method, $"api/directories/{graph.ChildA.Id}", new { name = "changed", parentId = graph.AlternateA.Id });

        await AssertStatus(HttpStatusCode.OK, response);
        var stored = await StoredDirectory(graph.ChildA.Id);
        Assert.Equal(graph.AlternateA.Id, stored.ParentId);
        Assert.Equal(graph.ProjectA.Id, stored.ProjectId);
        Assert.Equal("changed", stored.Name);
        Assert.Equal($"{graph.AlternateA.Path}{graph.ChildA.Id}/", stored.Path);
        Assert.Equal($"{stored.Path}{graph.GrandchildA.Id}/", (await StoredDirectory(graph.GrandchildA.Id)).Path);
    }

    /// <summary>An explicit null parent on a partial edit detaches the directory, and its descendants' paths follow.</summary>
    [Fact]
    public async Task PartialEdit_with_an_explicit_null_parent_makes_the_directory_top_level_and_moves_its_descendants_paths()
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PatchAsJsonAsync($"api/directories/{graph.ChildA.Id}",
            new Dictionary<string, object> { ["name"] = "changed", ["parentId"] = null }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        var stored = await StoredDirectory(graph.ChildA.Id);
        Assert.Null(stored.ParentId);
        Assert.Equal($"{graph.ChildA.Id}/", stored.Path);
        Assert.Equal($"{stored.Path}{graph.GrandchildA.Id}/", (await StoredDirectory(graph.GrandchildA.Id)).Path);
    }

    /// <summary>A partial edit that leaves the parent out leaves the directory, and its descendants, where they are.</summary>
    [Fact]
    public async Task PartialEdit_without_a_parent_keeps_the_directory_and_its_descendants_where_they_are()
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PatchAsJsonAsync($"api/directories/{graph.ChildA.Id}",
            new Dictionary<string, object> { ["name"] = "changed" }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        var stored = await StoredDirectory(graph.ChildA.Id);
        Assert.Equal(graph.DirectoryA.Id, stored.ParentId);
        Assert.Equal(graph.ChildA.Path, stored.Path);
        Assert.Equal($"{stored.Path}{graph.GrandchildA.Id}/", (await StoredDirectory(graph.GrandchildA.Id)).Path);
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
    public async Task Delete_is_forbidden_for_a_caller_holding_only_ViewProjects()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

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

    /// <summary>An export carries the settings manifest unless the caller asks it not to.</summary>
    [Fact]
    public async Task Export_writes_the_settings_manifest_by_default()
    {
        var (_, directory) = await SeedDirectory();
        await Seed(TestData.File(directory));

        var response = await RootClient.GetAsync($"api/directories/{directory.Id}/actions/export?archiveType=zip", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Contains(ArchiveService.ManifestEntryName, await ArchiveHelper.EntryNames(response, Ct));
    }

    [Fact]
    public async Task Export_with_includeSettings_false_writes_no_settings_manifest()
    {
        var (_, directory) = await SeedDirectory();
        await Seed(TestData.File(directory, ImportedFileName));

        var response = await RootClient.GetAsync($"api/directories/{directory.Id}/actions/export?archiveType=zip&includeSettings=false", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal([ImportedFileName], await ArchiveHelper.EntryNames(response, Ct));
    }

    /// <summary>A disk manifest describes the hypervisor, so including one needs ViewHosts as well.</summary>
    [Fact]
    public async Task Export_with_a_disk_manifest_is_forbidden_without_ViewHosts()
    {
        var (project, directory) = await SeedDirectory();
        await Seed(TestData.File(directory));
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/directories/{directory.Id}/actions/export?archiveType=zip&includeDiskManifest=true", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task GetDiskManifest_is_forbidden_without_ViewHosts()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/directories/{directory.Id}/disk-manifest", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task GetDiskManifest_returns_the_manifest_to_a_member_holding_ViewHosts()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor()
            .OnProject(project, [ProjectPermission.ViewProject])
            .WithSystemPermissions(SystemPermission.ViewHosts)
            .SeedAsync();

        var response = await Client(actor).GetAsync($"api/directories/{directory.Id}/disk-manifest", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
    }

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
    public async Task Export_returns_a_zip_to_a_caller_holding_ViewProjects()
    {
        var (_, directory) = await SeedDirectory();
        await Seed(TestData.File(directory));
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var response = await Client(actor).GetAsync($"api/directories/{directory.Id}/actions/export?archiveType=zip", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Export_is_forbidden_for_a_caller_holding_only_EditProjects()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Client(actor).GetAsync($"api/directories/{directory.Id}/actions/export?archiveType=zip", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
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

    /// <summary>
    /// A zip compressed in macOS Finder carries __MACOSX and .DS_Store files whose NUL bytes the
    /// database refused, failing the whole import with a 500. They are now left out, and any other
    /// binary file is left out with a warning.
    /// </summary>
    [Fact]
    public async Task Import_of_a_Finder_compressed_zip_stores_only_its_text_files()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var archive = ArchiveHelper.Zip(
            (ImportedFileName, Encoding.UTF8.GetBytes(ImportedContent)),
            (".DS_Store", new byte[] { 0x00, 0x00, 0x00, 0x01, 0x42, 0x75, 0x64, 0x31 }),
            ("__MACOSX/._" + ImportedFileName, new byte[] { 0x00, 0x05, 0x16, 0x07, 0x00, 0x02, 0x00, 0x00 }),
            ("diagram.png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00 }));

        var response = await Client(actor).PostAsync(ImportUrl(directory), archive, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(["'diagram.png' is a binary file and was not imported."], (await ReadAsync<ImportResultView>(response)).Warnings);

        await using var context = NewContext();
        Assert.Equal([ImportedFileName], await context.Files.Where(x => x.DirectoryId == directory.Id).Select(x => x.Name).ToListAsync(Ct));
        Assert.False(await context.Directories.AnyAsync(x => x.ParentId == directory.Id, Ct));
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
    public async Task Import_is_forbidden_for_a_caller_holding_only_ViewProjects()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

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

    /// <summary>The import's LockFiles check on an existing file with new content admits a caller holding the system LockFiles.</summary>
    [Fact]
    public async Task Import_by_a_caller_holding_EditProjects_and_LockFiles_replaces_an_administratively_locked_file()
    {
        // Gate: src/Caster.Api/Domain/Services/ImportService.cs#ImportService.UpdateFile
        var (_, directory) = await SeedDirectory();
        await SeedAdministrativelyLockedFile(directory);
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects, SystemPermission.LockFiles).SeedAsync();

        var response = await Client(actor).PostAsync(ImportUrl(directory), ArchiveHelper.Zip(ImportedFileName, ImportedContent), Ct);

        Assert.Empty((await ReadAsync<ImportResultView>(response)).LockedFiles);
        Assert.Equal(ImportedContent, await StoredContent(directory.Id, ImportedFileName));
    }

    /// <summary>The import's LockFiles check refuses a caller holding EditProjects without LockFiles, so the locked file is listed and kept.</summary>
    [Fact]
    public async Task Import_by_a_caller_holding_only_EditProjects_leaves_an_administratively_locked_file_and_lists_it()
    {
        // Gate: src/Caster.Api/Domain/Services/ImportService.cs#ImportService.UpdateFile
        var (_, directory) = await SeedDirectory();
        var file = await SeedAdministrativelyLockedFile(directory);
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Client(actor).PostAsync(ImportUrl(directory), ArchiveHelper.Zip(ImportedFileName, ImportedContent), Ct);

        Assert.Equal([LockedPath(directory)], (await ReadAsync<ImportResultView>(response)).LockedFiles);
        Assert.Equal(file.Content, await StoredContent(directory.Id, ImportedFileName));
    }

    /// <summary>The import's LockFiles check refuses a member whose LockFiles is on another project, so the locked file is listed and kept.</summary>
    [Fact]
    public async Task Import_by_a_member_holding_LockFiles_only_on_another_project_leaves_an_administratively_locked_file_and_lists_it()
    {
        // Gate: src/Caster.Api/Domain/Services/ImportService.cs#ImportService.UpdateFile
        var (project, directory) = await SeedDirectory();
        var file = await SeedAdministrativelyLockedFile(directory);
        var actor = await Actor()
            .OnProject(project, [ProjectPermission.EditProject])
            .OnNewProject(ProjectPermission.LockFiles)
            .SeedAsync();

        var response = await Client(actor).PostAsync(ImportUrl(directory), ArchiveHelper.Zip(ImportedFileName, ImportedContent), Ct);

        Assert.Equal([LockedPath(directory)], (await ReadAsync<ImportResultView>(response)).LockedFiles);
        Assert.Equal(file.Content, await StoredContent(directory.Id, ImportedFileName));
    }

    /// <summary>A member holding EditProject and LockFiles on the project is listed for an administratively locked file, which keeps its content.</summary>
    [Fact]
    public async Task Import_by_a_member_holding_EditProject_and_LockFiles_leaves_an_administratively_locked_file_and_lists_it()
    {
        var (project, directory) = await SeedDirectory();
        var file = await SeedAdministrativelyLockedFile(directory);
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject, ProjectPermission.LockFiles]).SeedAsync();

        var response = await Client(actor).PostAsync(ImportUrl(directory), ArchiveHelper.Zip(ImportedFileName, ImportedContent), Ct);

        Assert.Equal([LockedPath(directory)], (await ReadAsync<ImportResultView>(response)).LockedFiles);
        Assert.Equal(file.Content, await StoredContent(directory.Id, ImportedFileName));
    }

    [Fact]
    public async Task Import_leaves_a_file_another_user_holds_and_lists_it()
    {
        var (project, directory) = await SeedDirectory();
        var holder = await Actor().WithName("Holder").OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        var file = TestData.File(directory, ImportedFileName);
        file.Lock(holder.Id, canLock: false);
        await Seed(file);
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsync(ImportUrl(directory), ArchiveHelper.Zip(ImportedFileName, ImportedContent), Ct);

        Assert.Equal([LockedPath(directory)], (await ReadAsync<ImportResultView>(response)).LockedFiles);
        Assert.Equal(file.Content, await StoredContent(directory.Id, ImportedFileName));
    }

    [Fact]
    public async Task Import_creates_a_missing_workspace_and_child_directory_with_the_settings_the_archive_carries()
    {
        var (_, target) = await SeedDirectory(SourceDirectoryName);

        var response = await RootClient.PostAsync(ImportUrl(target), await SettingsArchive(SourceDirectory()), Ct);

        var result = await ReadAsync<ImportResultView>(response);
        Assert.Empty(result.SkippedSettings);
        Assert.Empty(result.Warnings);
        var stored = await ReadBack(db => db.Directories
            .Include(x => x.Workspaces)
            .Include(x => x.Children)
            .SingleAsync(x => x.Id == target.Id, Ct));
        var workspace = Assert.Single(stored.Workspaces);
        Assert.Equal("default", workspace.Name);
        Assert.Equal(InstalledVersion, workspace.TerraformVersion);
        Assert.Equal(2, workspace.Parallelism);
        Assert.Equal(5, workspace.AzureDestroyFailureThreshold);
        var child = Assert.Single(stored.Children);
        Assert.Equal("child", child.Name);
        Assert.Equal(InstalledVersion, child.TerraformVersion);
        Assert.Equal(7, child.Parallelism);
    }

    [Fact]
    public async Task Import_answers_a_warning_for_an_archive_exported_from_a_directory_of_another_name()
    {
        var (_, target) = await SeedDirectory("elsewhere");

        var response = await RootClient.PostAsync(ImportUrl(target), await SettingsArchive(SourceDirectory()), Ct);

        var result = await ReadAsync<ImportResultView>(response);
        Assert.Equal([$"This archive was exported from '{SourceDirectoryName}', which does not match 'elsewhere'."], result.Warnings);
    }

    [Fact]
    public async Task Import_leaves_a_version_the_host_does_not_have_unpinned_and_answers_it_as_skipped()
    {
        var (_, target) = await SeedDirectory(SourceDirectoryName);

        var response = await RootClient.PostAsync(ImportUrl(target), await SettingsArchive(SourceDirectory(NotInstalledVersion)), Ct);

        var result = await ReadAsync<ImportResultView>(response);
        Assert.Equal(2, result.SkippedSettings.Length);
        Assert.All(result.SkippedSettings, x => Assert.Contains(NotInstalledVersion, x, StringComparison.Ordinal));
        var child = await ReadBack(db => db.Directories.SingleAsync(x => x.ParentId == target.Id, Ct));
        Assert.Null(child.TerraformVersion);
        Assert.Equal(7, child.Parallelism);
    }

    /// <summary>A member holding only ImportProject is refused an import.</summary>
    // Same case as ProjectRequestTests.Import_is_forbidden_for_a_caller_holding_only_ImportProject.
    [Fact]
    public async Task Import_is_forbidden_for_a_caller_holding_only_ImportProject()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.ImportProject]).SeedAsync();

        var response = await Client(actor).PostAsync(ImportUrl(directory), ArchiveHelper.Zip(ImportedFileName, ImportedContent), Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    // ---- helpers --------------------------------------------------------------------------------

    private Task<TwoProjectGraph> SeedGraph() => TwoProjectGraph.SeedAsync(Db, Root.Id, Ct);

    /// <summary>Project A's four directories.</summary>
    private static Guid[] DirectoriesOf(TwoProjectGraph graph) =>
        [graph.DirectoryA.Id, graph.ChildA.Id, graph.GrandchildA.Id, graph.AlternateA.Id];

    /// <summary>ChildA is still under DirectoryA in project A, by its old name, and neither its path nor GrandchildA's moved.</summary>
    private async Task AssertChildUnchanged(TwoProjectGraph graph)
    {
        var stored = await StoredDirectory(graph.ChildA.Id);
        Assert.Equal(graph.DirectoryA.Id, stored.ParentId);
        Assert.Equal(graph.ProjectA.Id, stored.ProjectId);
        Assert.Equal("child", stored.Name);
        Assert.Equal(graph.ChildA.Path, stored.Path);
        Assert.Equal(graph.GrandchildA.Path, (await StoredDirectory(graph.GrandchildA.Id)).Path);
    }

    private Task<HttpResponseMessage> Send(TestActor actor, string method, string url, object body) =>
        Client(actor).SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = JsonContent.Create(body) }, Ct);


    /// <summary>
    /// A directory as an export reads it, never saved here: a workspace and a child directory, each holding a file
    /// and carrying settings off their defaults.
    /// </summary>
    private static Directory SourceDirectory(string terraformVersion = InstalledVersion)
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project, SourceDirectoryName);
        var workspace = TestData.Workspace(directory, "default");
        workspace.TerraformVersion = terraformVersion;
        workspace.Parallelism = 2;
        workspace.AzureDestroyFailureThreshold = 5;
        workspace.Files.Add(TestData.File(directory, "workspace.tf", ImportedContent));
        directory.Workspaces.Add(workspace);
        var child = TestData.Directory(project, "child", directory);
        child.TerraformVersion = terraformVersion;
        child.Parallelism = 7;
        child.Files.Add(TestData.File(child, ImportedFileName, ImportedContent));
        directory.Children.Add(child);

        return directory;
    }

    /// <summary><paramref name="source"/> exported as a zip by the host's own <c>IArchiveService</c>, as the export endpoint writes it.</summary>
    private async Task<MultipartFormDataContent> SettingsArchive(Directory source)
    {
        using var scope = Factory.Services.CreateScope();
        var archive = await scope.ServiceProvider.GetRequiredService<IArchiveService>()
            .ArchiveDirectory(source, ArchiveType.zip, includeIds: false, includeSettings: true);

        using var buffer = new MemoryStream();
        await archive.Data.CopyToAsync(buffer, Ct);

        return new MultipartFormDataContent { { new ByteArrayContent(buffer.ToArray()), "archive", archive.Name } };
    }

    private static string ImportUrl(Directory directory) => $"api/directories/{directory.Id}/actions/import";

    /// <summary>How an import names a file of <paramref name="directory"/> it could not update.</summary>
    private static string LockedPath(Directory directory) => $"{directory.Name}/{ImportedFileName}";

    /// <summary>A file of <paramref name="directory"/>, named as the imported one, that an administrator has locked.</summary>
    private async Task<File> SeedAdministrativelyLockedFile(Directory directory)
    {
        var file = TestData.File(directory, ImportedFileName);
        file.AdministrativelyLock(canLock: true);
        await Seed(file);

        return file;
    }

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

    private async Task<(Project Project, Directory Directory)> SeedDirectory(string name = "test-directory")
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project, name);
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
