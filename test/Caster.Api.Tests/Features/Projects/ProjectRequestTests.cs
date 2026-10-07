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
using ProjectView = Caster.Api.Features.Projects.Project;

namespace Caster.Api.Tests.Features.Projects;

/// <summary>
/// The project endpoints of <c>ProjectsController</c> (<c>api/projects</c>): each handler's
/// <c>Authorize()</c> gate, allowed through the system permission and through a project membership, and
/// refused for the near misses, then what a write stores.
/// </summary>
public class ProjectRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    private const string SeededDirectoryName = "network";

    private const string SeededFileName = "main.tf";

    /// <summary>Content no seeded file has, so a read-back proves the import wrote it.</summary>
    private const string ImportedContent = "# imported";

    // ---- GET api/projects/{id} ------------------------------------------------------------------

    [Fact]
    public async Task Get_returns_the_project_to_a_member_holding_ViewProject()
    {
        var project = await SeedProject();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/projects/{project.Id}", Ct);

        Assert.Equal(project.Id, (await ReadAsync<ProjectView>(response)).Id);
    }

    [Fact]
    public async Task Get_returns_the_project_to_a_caller_holding_ViewProjects()
    {
        var project = await SeedProject();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var response = await Client(actor).GetAsync($"api/projects/{project.Id}", Ct);

        Assert.Equal(project.Id, (await ReadAsync<ProjectView>(response)).Id);
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var project = await SeedProject();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/projects/{project.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_ViewProject_only_on_another_project()
    {
        var project = await SeedProject();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        var response = await Client(actor).GetAsync($"api/projects/{project.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_only_EditProjects()
    {
        var project = await SeedProject();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Client(actor).GetAsync($"api/projects/{project.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    /// <summary>A group the caller belongs to grants its project membership to the caller.</summary>
    [Fact]
    public async Task Get_returns_the_project_to_a_member_of_a_group_holding_ViewProject()
    {
        var project = await SeedProject();
        var actor = await Actor().OnProjectThroughNewGroup(project, ProjectPermission.ViewProject).SeedAsync();

        var response = await Client(actor).GetAsync($"api/projects/{project.Id}", Ct);

        Assert.Equal(project.Id, (await ReadAsync<ProjectView>(response)).Id);
    }

    [Fact]
    public async Task Get_of_an_unknown_project_is_not_found_for_a_caller_holding_ViewProjects()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var response = await Client(actor).GetAsync($"api/projects/{Guid.NewGuid()}", Ct);

        await AssertProblem(HttpStatusCode.NotFound, response);
    }

    // ---- GET api/projects -----------------------------------------------------------------------

    [Fact]
    public async Task GetAll_returns_every_project_to_a_caller_holding_ViewProjects()
    {
        var project = await SeedProject();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var response = await Client(actor).GetAsync("api/projects", Ct);

        Assert.Contains(project.Id, (await ReadAsync<ProjectView[]>(response)).Select(x => x.Id));
    }

    [Fact]
    public async Task GetAll_is_forbidden_for_a_caller_holding_only_ViewProject_on_a_project()
    {
        var project = await SeedProject();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync("api/projects", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task GetAll_is_forbidden_for_a_caller_holding_only_EditProjects()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Client(actor).GetAsync("api/projects", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    /// <summary><c>onlyMine</c> needs no permission and lists the projects the caller is a member of.</summary>
    [Fact]
    public async Task GetAll_with_onlyMine_returns_only_the_callers_projects()
    {
        var mine = await SeedProject("Mine");
        var other = await SeedProject("Other");
        var actor = await Actor().OnProject(mine, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync("api/projects?onlyMine=true", Ct);

        var ids = (await ReadAsync<ProjectView[]>(response)).Select(x => x.Id).ToArray();
        Assert.Contains(mine.Id, ids);
        Assert.DoesNotContain(other.Id, ids);
    }

    // ---- POST api/projects ----------------------------------------------------------------------

    [Fact]
    public async Task Create_by_a_caller_holding_CreateProjects_stores_the_project_and_makes_the_creator_its_manager()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.CreateProjects).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/projects", new { name = "Created", description = "d" }, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        var created = await ReadAsync<ProjectView>(response);
        await using var context = NewContext();
        var stored = await context.Projects.Include(x => x.Memberships).SingleAsync(x => x.Id == created.Id, Ct);
        Assert.Equal("Created", stored.Name);
        var membership = Assert.Single(stored.Memberships);
        Assert.Equal(actor.Id, membership.UserId);
        Assert.Equal(TestData.ProjectRoles.Manager, membership.RoleId);
    }

    [Fact]
    public async Task Create_keeps_the_id_the_caller_chose()
    {
        var id = Guid.NewGuid();

        var response = await RootClient.PostAsJsonAsync("api/projects", new { id, name = "Chosen" }, Ct);

        Assert.Equal(id, (await ReadAsync<ProjectView>(response)).Id);
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_caller_holding_only_ManageProjects()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageProjects).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/projects", new { name = "Refused" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        await using var context = NewContext();
        Assert.False(await context.Projects.AnyAsync(x => x.Name == "Refused", Ct));
    }

    /// <summary>An id another project already has is answered with a 500 on create.</summary>
    [Fact]
    public async Task Create_answers_an_id_that_is_already_taken_with_a_server_error()
    {
        var project = await SeedProject("Taken");

        var response = await RootClient.PostAsJsonAsync("api/projects", new { id = project.Id, name = "Again" }, Ct);

        await AssertProblem(HttpStatusCode.InternalServerError, response);
    }

    // ---- PUT api/projects/{id} ------------------------------------------------------------------

    [Fact]
    public async Task Edit_by_a_member_holding_EditProject_stores_the_new_name()
    {
        var project = await SeedProject("Before");
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/projects/{project.Id}", new { name = "After" }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("After", await StoredName(project.Id));
    }

    [Fact]
    public async Task Edit_by_a_caller_holding_EditProjects_stores_the_new_name()
    {
        var project = await SeedProject("Before");
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/projects/{project.Id}", new { name = "After" }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("After", await StoredName(project.Id));
    }

    [Fact]
    public async Task Edit_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var project = await SeedProject("Before");
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/projects/{project.Id}", new { name = "After" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal("Before", await StoredName(project.Id));
    }

    [Fact]
    public async Task Edit_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var project = await SeedProject("Before");
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/projects/{project.Id}", new { name = "After" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal("Before", await StoredName(project.Id));
    }

    [Fact]
    public async Task Edit_is_forbidden_for_a_caller_holding_only_ViewProjects()
    {
        var project = await SeedProject("Before");
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/projects/{project.Id}", new { name = "After" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    // ---- DELETE api/projects/{id} ---------------------------------------------------------------

    /// <summary>A member holding EditProject, and not ManageProject, deletes the whole project.</summary>
    [Fact]
    public async Task Delete_by_a_member_holding_only_EditProject_removes_the_project()
    {
        var project = await SeedProject();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/projects/{project.Id}", Ct);

        await AssertStatus(HttpStatusCode.NoContent, response);
        Assert.False(await ProjectExists(project.Id));
    }

    [Fact]
    public async Task Delete_by_a_caller_holding_EditProjects_removes_the_project()
    {
        var project = await SeedProject();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/projects/{project.Id}", Ct);

        await AssertStatus(HttpStatusCode.NoContent, response);
        Assert.False(await ProjectExists(project.Id));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var project = await SeedProject();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/projects/{project.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.True(await ProjectExists(project.Id));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var project = await SeedProject();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/projects/{project.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.True(await ProjectExists(project.Id));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_holding_only_ManageProjects()
    {
        var project = await SeedProject();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageProjects).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/projects/{project.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.True(await ProjectExists(project.Id));
    }

    [Fact]
    public async Task Delete_of_a_project_with_a_queued_run_is_a_conflict()
    {
        var project = await SeedProject();
        var directory = TestData.Directory(project);
        var workspace = TestData.Workspace(directory);
        await Seed(directory, workspace, TestData.Run(workspace));

        var response = await RootClient.DeleteAsync($"api/projects/{project.Id}", Ct);

        await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.True(await ProjectExists(project.Id));
    }

    // ---- GET api/projects/projects/{id}/actions/export ------------------------------------------

    [Fact]
    public async Task Export_returns_a_zip_to_a_member_holding_ViewProject()
    {
        var project = await SeedProjectWithAFile();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync(ExportUrl(project.Id), Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Export_returns_a_zip_to_a_caller_holding_ViewProjects()
    {
        var project = await SeedProjectWithAFile();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var response = await Client(actor).GetAsync(ExportUrl(project.Id), Ct);

        await AssertStatus(HttpStatusCode.OK, response);
    }

    [Fact]
    public async Task Export_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var project = await SeedProjectWithAFile();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).GetAsync(ExportUrl(project.Id), Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Export_is_forbidden_for_a_caller_holding_ViewProject_only_on_another_project()
    {
        var project = await SeedProjectWithAFile();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        var response = await Client(actor).GetAsync(ExportUrl(project.Id), Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    // ---- POST api/projects/projects/{id}/actions/import -------------------------------------------

    [Fact]
    public async Task Import_by_a_member_holding_EditProject_replaces_the_content_of_a_file_of_the_same_name()
    {
        var project = await SeedProjectWithAFile();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsync(ImportUrl(project.Id), Archive(), Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(ImportedContent, await StoredContent(project.Id));
    }

    [Fact]
    public async Task Import_by_a_caller_holding_ImportProjects_replaces_the_content_of_a_file_of_the_same_name()
    {
        var project = await SeedProjectWithAFile();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ImportProjects).SeedAsync();

        var response = await Client(actor).PostAsync(ImportUrl(project.Id), Archive(), Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(ImportedContent, await StoredContent(project.Id));
    }

    /// <summary>A member holding only ImportProject is refused an import.</summary>
    [Fact]
    public async Task Import_is_forbidden_for_a_caller_holding_only_ImportProject()
    {
        var project = await SeedProjectWithAFile();
        var actor = await Actor().OnProject(project, [ProjectPermission.ImportProject]).SeedAsync();

        var response = await Client(actor).PostAsync(ImportUrl(project.Id), Archive(), Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Import_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var project = await SeedProjectWithAFile();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).PostAsync(ImportUrl(project.Id), Archive(), Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Import_is_forbidden_for_a_caller_holding_only_EditProjects()
    {
        var project = await SeedProjectWithAFile();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Client(actor).PostAsync(ImportUrl(project.Id), Archive(), Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static string ImportUrl(Guid projectId) => $"api/projects/projects/{projectId}/actions/import";

    /// <summary>A project archive holding <see cref="ImportedContent"/> for the file <see cref="SeedProjectWithAFile"/> seeds.</summary>
    private static MultipartFormDataContent Archive() =>
        ArchiveHelper.Zip($"{SeededDirectoryName}/{SeededFileName}", ImportedContent);

    private async Task<string> StoredContent(Guid projectId)
    {
        await using var context = NewContext();

        return (await context.Files.SingleAsync(x => x.Directory.ProjectId == projectId && x.Name == SeededFileName, Ct)).Content;
    }

    private static string ExportUrl(Guid projectId) =>
        $"api/projects/projects/{projectId}/actions/export?archiveType=zip";

    private async Task<Project> SeedProject(string name = "Test Project")
    {
        var project = TestData.Project(name);
        await Seed(project);

        return project;
    }

    private async Task<Project> SeedProjectWithAFile()
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project, SeededDirectoryName);
        await Seed(project, directory, TestData.File(directory, SeededFileName));

        return project;
    }

    private async Task<string> StoredName(Guid projectId)
    {
        await using var context = NewContext();

        return (await context.Projects.SingleAsync(x => x.Id == projectId, Ct)).Name;
    }

    private async Task<bool> ProjectExists(Guid projectId)
    {
        await using var context = NewContext();

        return await context.Projects.AnyAsync(x => x.Id == projectId, Ct);
    }
}
