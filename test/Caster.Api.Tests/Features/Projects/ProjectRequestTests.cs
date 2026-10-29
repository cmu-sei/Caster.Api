// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Directory = Caster.Api.Domain.Models.Directory;
using ImportProjectResult = Caster.Api.Features.Projects.Import.ImportProjectResult;
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

    /// <summary>The project the settings archives are exported from.</summary>
    private const string ImportSourceName = "Range Alpha";

    /// <summary>The project they are imported into, named otherwise unless a test is about the name.</summary>
    private const string ImportTargetName = "Range Beta";

    private const string ImportedDirectoryName = "Infrastructure";

    private const string ImportedWorkspaceName = "default";

    private const string ImportedWorkspaceFileName = "workspace.tf";

    /// <summary>A Terraform version the host has: <c>TestConfiguration</c> gives <c>Terraform:BinaryPath</c> a folder for it.</summary>
    private const string InstalledVersion = "1.5.7";

    /// <summary>A well-formed Terraform version <c>Terraform:BinaryPath</c> has no folder for.</summary>
    private const string NotInstalledVersion = "1.9.9";

    /// <summary>A version stored on a directory and workspace that already exist, unlike any version an archive carries.</summary>
    private const string OtherVersion = "0.14.0";

    // ---- GET api/projects/{id} ----------------------------------------------------------------------

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

    // ---- GET api/projects ---------------------------------------------------------------------------

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

    // ---- POST api/projects --------------------------------------------------------------------------

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

        var problem = await AssertProblem(HttpStatusCode.InternalServerError, response);

        Assert.Equal("An error occurred while saving the entity changes. See the inner exception for details.", problem.Detail);
    }

    // ---- PUT api/projects/{id} ----------------------------------------------------------------------

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

    // ---- DELETE api/projects/{id} -------------------------------------------------------------------

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

    // ---- GET api/projects/{id}/actions/export -------------------------------------------------------

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
    public async Task Export_is_forbidden_for_a_caller_holding_only_EditProjects()
    {
        var project = await SeedProjectWithAFile();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

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

    /// <summary>An export carries the settings manifest unless the caller asks it not to.</summary>
    [Fact]
    public async Task Export_writes_the_settings_manifest_by_default()
    {
        var project = await SeedProjectWithAFile();

        var response = await RootClient.GetAsync(ExportUrl(project.Id), Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Contains(ArchiveService.ManifestEntryName, await ArchiveHelper.EntryNames(response, Ct));
    }

    [Fact]
    public async Task Export_with_includeSettings_false_writes_no_settings_manifest()
    {
        var project = await SeedProjectWithAFile();

        var response = await RootClient.GetAsync($"{ExportUrl(project.Id)}&includeSettings=false", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        var entries = await ArchiveHelper.EntryNames(response, Ct);
        Assert.DoesNotContain(ArchiveService.ManifestEntryName, entries);
        Assert.Contains($"{SeededDirectoryName}/{SeededFileName}", entries);
    }

    /// <summary>A disk manifest describes the hypervisor, so including one needs ViewHosts as well.</summary>
    [Fact]
    public async Task Export_with_a_disk_manifest_is_forbidden_without_ViewHosts()
    {
        var project = await SeedProjectWithAFile();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"{ExportUrl(project.Id)}&includeDiskManifest=true", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Export_with_a_disk_manifest_writes_it_for_a_caller_holding_ViewHosts()
    {
        var project = await SeedProjectWithAFile();
        var actor = await Actor()
            .OnProject(project, [ProjectPermission.ViewProject])
            .WithSystemPermissions(SystemPermission.ViewHosts)
            .SeedAsync();

        var response = await Client(actor).GetAsync($"{ExportUrl(project.Id)}&includeDiskManifest=true", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Contains(ArchiveService.DiskManifestEntryName, await ArchiveHelper.EntryNames(response, Ct));
    }

    // ---- GET api/projects/{id}/disk-manifest ------------------------------------------------------

    [Fact]
    public async Task GetDiskManifest_is_forbidden_without_ViewHosts()
    {
        var project = await SeedProjectWithAFile();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/projects/{project.Id}/disk-manifest", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task GetDiskManifest_is_forbidden_with_ViewHosts_but_not_ViewProject()
    {
        var project = await SeedProjectWithAFile();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewHosts).SeedAsync();

        var response = await Client(actor).GetAsync($"api/projects/{project.Id}/disk-manifest", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task GetDiskManifest_returns_the_manifest_to_a_member_holding_ViewHosts()
    {
        var project = await SeedProjectWithAFile();
        var actor = await Actor()
            .OnProject(project, [ProjectPermission.ViewProject])
            .WithSystemPermissions(SystemPermission.ViewHosts)
            .SeedAsync();

        var response = await Client(actor).GetAsync($"api/projects/{project.Id}/disk-manifest", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
    }

    // ---- POST api/projects/{id}/actions/import ------------------------------------------------------

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

    // ---- POST api/projects/{id}/actions/import: the settings an archive carries ---------------------

    [Fact]
    public async Task Import_creates_a_missing_directory_and_workspace_with_the_settings_the_archive_carries()
    {
        var target = await SeedProject(ImportTargetName);

        var response = await RootClient.PostAsync(ImportUrl(target.Id), await SettingsArchive(SourceProject()), Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        var directory = await StoredImportedDirectory(target.Id);
        Assert.Equal(ImportedDirectoryName, directory.Name);
        Assert.Equal(InstalledVersion, directory.TerraformVersion);
        Assert.Equal(4, directory.Parallelism);
        Assert.Equal(3, directory.AzureDestroyFailureThreshold);
        Assert.True(directory.AzureDestroyFailureThresholdEnabled);
        var workspace = Assert.Single(directory.Workspaces);
        Assert.Equal(ImportedWorkspaceName, workspace.Name);
        Assert.Equal(InstalledVersion, workspace.TerraformVersion);
        Assert.Equal(2, workspace.Parallelism);
        Assert.Equal(5, workspace.AzureDestroyFailureThreshold);
        // What belongs to the environment an archive came from never travels with it.
        Assert.Null(workspace.HostId);
        Assert.False(workspace.DynamicHost);
        Assert.Null(workspace.State);
        Assert.Null(workspace.StateBackup);
    }

    [Fact]
    public async Task Import_keeps_the_settings_of_a_directory_and_workspace_that_already_exist()
    {
        var target = TestData.Project(ImportTargetName);
        var directory = TestData.Directory(target, ImportedDirectoryName);
        directory.TerraformVersion = OtherVersion;
        directory.Parallelism = 9;
        var workspace = TestData.Workspace(directory, ImportedWorkspaceName);
        workspace.TerraformVersion = OtherVersion;
        workspace.Parallelism = 9;
        await Seed(target, directory, workspace);

        var response = await RootClient.PostAsync(ImportUrl(target.Id), await SettingsArchive(SourceProject()), Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        var stored = await StoredImportedDirectory(target.Id);
        Assert.Equal(OtherVersion, stored.TerraformVersion);
        Assert.Equal(9, stored.Parallelism);
        var storedWorkspace = Assert.Single(stored.Workspaces);
        Assert.Equal(OtherVersion, storedWorkspace.TerraformVersion);
        Assert.Equal(9, storedWorkspace.Parallelism);
        Assert.Contains(stored.Files, x => x.Name == SeededFileName && x.Content == ImportedContent);
    }

    [Fact]
    public async Task Import_of_an_archive_without_settings_creates_the_directory_and_workspace_unpinned()
    {
        var target = await SeedProject(ImportTargetName);

        var response = await RootClient.PostAsync(ImportUrl(target.Id), await SettingsArchive(SourceProject(), includeSettings: false), Ct);

        var result = await ReadAsync<ImportProjectResult>(response);
        Assert.Empty(result.SkippedSettings);
        Assert.Empty(result.Warnings);
        var directory = await StoredImportedDirectory(target.Id);
        Assert.Null(directory.TerraformVersion);
        Assert.Contains(directory.Files, x => x.Name == SeededFileName);
        var workspace = Assert.Single(directory.Workspaces);
        Assert.Null(workspace.TerraformVersion);
        Assert.Null(workspace.Parallelism);
        Assert.Contains(workspace.Files, x => x.Name == ImportedWorkspaceFileName);
    }

    [Fact]
    public async Task Import_leaves_a_version_the_host_does_not_have_unpinned_and_answers_it_as_skipped()
    {
        var target = await SeedProject(ImportTargetName);

        var response = await RootClient.PostAsync(ImportUrl(target.Id), await SettingsArchive(SourceProject(NotInstalledVersion)), Ct);

        var result = await ReadAsync<ImportProjectResult>(response);
        Assert.Equal(2, result.SkippedSettings.Length);
        Assert.All(result.SkippedSettings, x => Assert.Contains(NotInstalledVersion, x, StringComparison.Ordinal));
        var directory = await StoredImportedDirectory(target.Id);
        Assert.Null(directory.TerraformVersion);
        Assert.Null(Assert.Single(directory.Workspaces).TerraformVersion);
        // The settings that are still valid are applied.
        Assert.Equal(4, directory.Parallelism);
    }

    [Fact]
    public async Task Import_answers_a_warning_for_an_archive_exported_from_a_project_of_another_name()
    {
        var target = await SeedProject(ImportTargetName);

        var response = await RootClient.PostAsync(ImportUrl(target.Id), await SettingsArchive(SourceProject()), Ct);

        var result = await ReadAsync<ImportProjectResult>(response);
        Assert.Equal([$"This archive was exported from '{ImportSourceName}', which does not match '{ImportTargetName}'."], result.Warnings);
        Assert.Empty(result.SkippedSettings);
    }

    [Fact]
    public async Task Import_answers_no_warning_for_an_archive_exported_from_a_project_of_the_same_name()
    {
        var target = await SeedProject(ImportSourceName);

        var response = await RootClient.PostAsync(ImportUrl(target.Id), await SettingsArchive(SourceProject()), Ct);

        var result = await ReadAsync<ImportProjectResult>(response);
        Assert.Empty(result.Warnings);
    }

    // ---- api/projects/projects/{id}/actions/export|import: the obsolete aliases ---------------------

    /// <summary>The double-prefixed export route stays as an alias of the corrected one.</summary>
    [Fact]
    public async Task Export_through_the_obsolete_route_returns_a_zip_to_a_member_holding_ViewProject()
    {
        var project = await SeedProjectWithAFile();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync(ObsoleteExportUrl(project.Id), Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Export_through_the_obsolete_route_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var project = await SeedProjectWithAFile();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).GetAsync(ObsoleteExportUrl(project.Id), Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    /// <summary>The double-prefixed import route stays as an alias of the corrected one.</summary>
    [Fact]
    public async Task Import_through_the_obsolete_route_by_a_member_holding_EditProject_replaces_the_content_of_a_file()
    {
        var project = await SeedProjectWithAFile();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsync(ObsoleteImportUrl(project.Id), Archive(), Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(ImportedContent, await StoredContent(project.Id));
    }

    [Fact]
    public async Task Import_through_the_obsolete_route_is_forbidden_for_a_caller_holding_only_ImportProject()
    {
        var project = await SeedProjectWithAFile();
        var actor = await Actor().OnProject(project, [ProjectPermission.ImportProject]).SeedAsync();

        var response = await Client(actor).PostAsync(ObsoleteImportUrl(project.Id), Archive(), Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.NotEqual(ImportedContent, await StoredContent(project.Id));
    }

    // ---- helpers ------------------------------------------------------------------------------------

    /// <summary>
    /// A project as an export reads it, never saved here: one top-level directory holding <see cref="SeededFileName"/>
    /// and one workspace with a file of its own, with every setting an archive carries set off its default.
    /// </summary>
    private static Project SourceProject(string terraformVersion = InstalledVersion)
    {
        var project = TestData.Project(ImportSourceName);
        var directory = TestData.Directory(project, ImportedDirectoryName);
        directory.TerraformVersion = terraformVersion;
        directory.Parallelism = 4;
        directory.AzureDestroyFailureThresholdEnabled = true;
        directory.AzureDestroyFailureThreshold = 3;
        directory.Files.Add(TestData.File(directory, SeededFileName, ImportedContent));
        var workspace = TestData.Workspace(directory, ImportedWorkspaceName);
        workspace.TerraformVersion = terraformVersion;
        workspace.Parallelism = 2;
        workspace.AzureDestroyFailureThreshold = 5;
        workspace.Files.Add(TestData.File(directory, ImportedWorkspaceFileName, ImportedContent));
        directory.Workspaces.Add(workspace);
        project.Directories.Add(directory);

        return project;
    }

    /// <summary>
    /// <paramref name="source"/> exported as a zip by the host's own <c>IArchiveService</c>, as the export endpoint
    /// writes it, under the form field the import endpoint binds.
    /// </summary>
    private async Task<MultipartFormDataContent> SettingsArchive(Project source, bool includeSettings = true)
    {
        using var scope = Factory.Services.CreateScope();
        var archive = await scope.ServiceProvider.GetRequiredService<IArchiveService>()
            .ArchiveProject(source, ArchiveType.zip, includeIds: false, includeSettings);

        using var buffer = new MemoryStream();
        await archive.Data.CopyToAsync(buffer, Ct);

        return new MultipartFormDataContent { { new ByteArrayContent(buffer.ToArray()), "archive", archive.Name } };
    }

    /// <summary>The one top-level directory of <paramref name="projectId"/>, with its files and workspaces and theirs.</summary>
    private Task<Directory> StoredImportedDirectory(Guid projectId) =>
        ReadBack(db => db.Directories
            .Include(x => x.Files)
            .Include(x => x.Workspaces).ThenInclude(x => x.Files)
            .SingleAsync(x => x.ProjectId == projectId && x.ParentId == null, Ct));


    private static string ImportUrl(Guid projectId) => $"api/projects/{projectId}/actions/import";

    private static string ObsoleteImportUrl(Guid projectId) => $"api/projects/projects/{projectId}/actions/import";

    /// <summary>A project archive holding <see cref="ImportedContent"/> for the file <see cref="SeedProjectWithAFile"/> seeds.</summary>
    private static MultipartFormDataContent Archive() =>
        ArchiveHelper.Zip($"{SeededDirectoryName}/{SeededFileName}", ImportedContent);

    private async Task<string> StoredContent(Guid projectId)
    {
        await using var context = NewContext();

        return (await context.Files.SingleAsync(x => x.Directory.ProjectId == projectId && x.Name == SeededFileName, Ct)).Content;
    }

    private static string ExportUrl(Guid projectId) =>
        $"api/projects/{projectId}/actions/export?archiveType=zip";

    private static string ObsoleteExportUrl(Guid projectId) =>
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
