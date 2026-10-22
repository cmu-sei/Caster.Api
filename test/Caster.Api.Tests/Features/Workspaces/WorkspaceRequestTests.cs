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
using WorkspaceView = Caster.Api.Features.Workspaces.Workspace;

namespace Caster.Api.Tests.Features.Workspaces;

/// <summary>
/// <c>WorkspacesController</c>: a workspace is read with ViewProject on its project (or ViewProjects) and
/// changed with EditProject (or EditProjects); the workspace-locking switch needs ViewWorkspaces to read and
/// ManageWorkspaces to set.
/// </summary>
public class WorkspaceRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    // ---- reads ----------------------------------------------------------------------------------

    [Fact]
    public async Task Get_returns_the_workspace_to_a_member_holding_ViewProject()
    {
        var (project, _, workspace) = await SeedWorkspace();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/workspaces/{workspace.Id}", Ct);

        Assert.Equal(workspace.Name, (await ReadAsync<WorkspaceView>(response)).Name);
    }

    [Fact]
    public async Task Get_returns_the_workspace_to_a_caller_holding_ViewProjects()
    {
        var (_, _, workspace) = await SeedWorkspace();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).GetAsync($"api/workspaces/{workspace.Id}", Ct));
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var (project, _, workspace) = await SeedWorkspace();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/workspaces/{workspace.Id}", Ct));
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_ViewProject_only_on_another_project()
    {
        var (_, _, workspace) = await SeedWorkspace();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/workspaces/{workspace.Id}", Ct));
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_only_ViewWorkspaces()
    {
        var (_, _, workspace) = await SeedWorkspace();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewWorkspaces).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/workspaces/{workspace.Id}", Ct));
    }

    [Fact]
    public async Task GetAll_lists_every_workspace_for_a_caller_holding_ViewProjects()
    {
        var (_, _, workspace) = await SeedWorkspace();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var response = await Client(actor).GetAsync("api/workspaces", Ct);

        Assert.Equal([workspace.Id], (await ReadAsync<WorkspaceView[]>(response)).Select(x => x.Id));
    }

    [Fact]
    public async Task GetAll_is_forbidden_for_a_member_holding_ViewProject_on_a_project()
    {
        var (project, _, _) = await SeedWorkspace();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync("api/workspaces", Ct));
    }

    [Fact]
    public async Task GetAll_is_forbidden_for_a_caller_holding_only_ViewWorkspaces()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewWorkspaces).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync("api/workspaces", Ct));
    }

    [Fact]
    public async Task GetByDirectory_lists_the_directorys_workspaces_for_a_member_holding_ViewProject()
    {
        var (project, directory, workspace) = await SeedWorkspace();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/directories/{directory.Id}/workspaces", Ct);

        Assert.Equal([workspace.Id], (await ReadAsync<WorkspaceView[]>(response)).Select(x => x.Id));
    }

    [Fact]
    public async Task GetByDirectory_lists_the_workspaces_for_a_caller_holding_ViewProjects()
    {
        var (_, directory, _) = await SeedWorkspace();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).GetAsync($"api/directories/{directory.Id}/workspaces", Ct));
    }

    [Fact]
    public async Task GetByDirectory_is_forbidden_for_a_caller_holding_ViewProject_only_on_another_project()
    {
        var (_, directory, _) = await SeedWorkspace();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/directories/{directory.Id}/workspaces", Ct));
    }

    [Fact]
    public async Task GetByDirectory_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var (project, directory, _) = await SeedWorkspace();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/directories/{directory.Id}/workspaces", Ct));
    }

    [Fact]
    public async Task GetByDirectory_is_forbidden_for_a_caller_holding_only_EditProjects()
    {
        var (_, directory, _) = await SeedWorkspace();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/directories/{directory.Id}/workspaces", Ct));
    }

    // ---- POST api/workspaces --------------------------------------------------------------------

    [Fact]
    public async Task Create_by_a_member_holding_EditProject_stores_the_workspace_with_the_default_version()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/workspaces", new { name = "created", directoryId = directory.Id }, Ct);

        var created = await ReadAsync<WorkspaceView>(response);
        var stored = await StoredWorkspace(created.Id);
        Assert.Equal(directory.Id, stored.DirectoryId);
        Assert.Equal(TestConfiguration.TerraformVersions[0], stored.TerraformVersion);
    }

    [Fact]
    public async Task Create_takes_the_terraform_version_its_directory_names()
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        directory.TerraformVersion = "1.5.7";
        await Seed(project, directory);

        var response = await RootClient.PostAsJsonAsync("api/workspaces", new { name = "inherits", directoryId = directory.Id }, Ct);

        Assert.Equal("1.5.7", (await ReadAsync<WorkspaceView>(response)).TerraformVersion);
    }

    /// <summary>Each setting cascades from the directory on its own: naming a version does not stop the parallelism cascading.</summary>
    [Fact]
    public async Task Create_with_a_terraform_version_takes_the_parallelism_its_directory_names()
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        directory.Parallelism = 7;
        await Seed(project, directory);

        var response = await RootClient.PostAsJsonAsync(
            "api/workspaces", new { name = "inherits", directoryId = directory.Id, terraformVersion = "1.5.7" }, Ct);

        var created = await ReadAsync<WorkspaceView>(response);
        var stored = await StoredWorkspace(created.Id);
        Assert.Equal("1.5.7", stored.TerraformVersion);
        Assert.Equal(7, stored.Parallelism);
    }

    [Fact]
    public async Task Create_answers_a_bad_request_for_a_name_an_archive_reserves()
    {
        var (_, directory) = await SeedDirectory();

        var response = await RootClient.PostAsJsonAsync("api/workspaces", new { name = "__Caster__", directoryId = directory.Id }, Ct);

        await AssertProblem(HttpStatusCode.BadRequest, response);
        Assert.False(await WorkspaceNamed("__Caster__"));
    }

    [Fact]
    public async Task Create_by_a_caller_holding_EditProjects_stores_the_workspace()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/workspaces", new { name = "created", directoryId = directory.Id }, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/workspaces", new { name = "refused", directoryId = directory.Id }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.False(await WorkspaceNamed("refused"));
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_caller_holding_only_ViewProjects()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/workspaces", new { name = "refused", directoryId = directory.Id }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.False(await WorkspaceNamed("refused"));
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/workspaces", new { name = "refused", directoryId = directory.Id }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.False(await WorkspaceNamed("refused"));
    }

    [Fact]
    public async Task Create_with_a_name_holding_a_slash_is_a_bad_request()
    {
        var (_, directory) = await SeedDirectory();

        var response = await RootClient.PostAsJsonAsync("api/workspaces", new { name = "../escape", directoryId = directory.Id }, Ct);

        await AssertProblem(HttpStatusCode.BadRequest, response);
        Assert.False(await WorkspaceNamed("../escape"));
    }

    [Fact]
    public async Task Create_with_a_terraform_version_that_is_not_installed_is_a_bad_request()
    {
        var (_, directory) = await SeedDirectory();

        var response = await RootClient.PostAsJsonAsync(
            "api/workspaces", new { name = "unversioned", directoryId = directory.Id, terraformVersion = "9.9.9" }, Ct);

        await AssertProblem(HttpStatusCode.BadRequest, response);
        Assert.False(await WorkspaceNamed("unversioned"));
    }

    [Fact]
    public async Task Create_broadcasts_the_workspace_to_its_projects_group()
    {
        var (project, directory) = await SeedDirectory();

        var response = await RootClient.PostAsJsonAsync("api/workspaces", new { name = "announced", directoryId = directory.Id }, Ct);

        var created = await ReadAsync<WorkspaceView>(response);
        var broadcast = Assert.Single(Factory.Hub<ProjectHub>().ToGroup(project.Id), x => x.Method == "WorkspaceCreated");
        Assert.Equal(created.Id, Assert.IsType<WorkspaceView>(broadcast.Arguments[0]).Id);
    }

    // ---- PUT api/workspaces/{id} ----------------------------------------------------------------

    [Fact]
    public async Task Edit_by_a_member_holding_EditProject_stores_the_new_name()
    {
        var (project, directory, workspace) = await SeedWorkspace();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/workspaces/{workspace.Id}", new { name = "renamed", directoryId = directory.Id }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("renamed", (await StoredWorkspace(workspace.Id)).Name);
    }

    [Fact]
    public async Task Edit_by_a_caller_holding_EditProjects_stores_the_new_name()
    {
        var (_, directory, workspace) = await SeedWorkspace();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/workspaces/{workspace.Id}", new { name = "renamed", directoryId = directory.Id }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
    }

    [Fact]
    public async Task Edit_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, directory, workspace) = await SeedWorkspace();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/workspaces/{workspace.Id}", new { name = "renamed", directoryId = directory.Id }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(workspace.Name, (await StoredWorkspace(workspace.Id)).Name);
    }

    [Fact]
    public async Task Edit_is_forbidden_for_a_caller_holding_only_ViewProjects()
    {
        var (_, directory, workspace) = await SeedWorkspace();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/workspaces/{workspace.Id}", new { name = "renamed", directoryId = directory.Id }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(workspace.Name, (await StoredWorkspace(workspace.Id)).Name);
    }

    [Fact]
    public async Task Edit_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, directory, workspace) = await SeedWorkspace();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/workspaces/{workspace.Id}", new { name = "renamed", directoryId = directory.Id }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(workspace.Name, (await StoredWorkspace(workspace.Id)).Name);
    }

    /// <summary>
    /// A caller holding EditProject only on another project is refused this project's workspace, though the
    /// body names a directory of its own: the workspace is authorized, not the body's directory.
    /// </summary>
    [Fact]
    public async Task Edit_is_forbidden_for_a_member_of_another_project_naming_its_own_directory()
    {
        var (_, directory, workspace) = await SeedWorkspace();
        var (theirs, theirDirectory) = await SeedDirectory();
        var actor = await Actor().OnProject(theirs, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync(
            $"api/workspaces/{workspace.Id}", new { name = "taken", directoryId = theirDirectory.Id }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        var stored = await StoredWorkspace(workspace.Id);
        Assert.Equal(workspace.Name, stored.Name);
        Assert.Equal(directory.Id, stored.DirectoryId);
    }

    // ---- PATCH api/workspaces/{id} --------------------------------------------------------------

    [Fact]
    public async Task PartialEdit_by_a_member_holding_EditProject_stores_only_the_version()
    {
        var (project, _, workspace) = await SeedWorkspace();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PatchAsJsonAsync($"api/workspaces/{workspace.Id}", new { terraformVersion = "1.5.7" }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        var stored = await StoredWorkspace(workspace.Id);
        Assert.Equal("1.5.7", stored.TerraformVersion);
        Assert.Equal(workspace.Name, stored.Name);
    }

    [Fact]
    public async Task PartialEdit_answers_a_bad_request_for_a_name_an_archive_reserves()
    {
        var (_, _, workspace) = await SeedWorkspace();

        var response = await RootClient.PatchAsJsonAsync($"api/workspaces/{workspace.Id}", new { name = "__Workspaces__" }, Ct);

        await AssertProblem(HttpStatusCode.BadRequest, response);
        Assert.Equal(workspace.Name, (await StoredWorkspace(workspace.Id)).Name);
    }

    [Fact]
    public async Task PartialEdit_by_a_caller_holding_EditProjects_stores_the_change()
    {
        var (_, _, workspace) = await SeedWorkspace();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).PatchAsJsonAsync($"api/workspaces/{workspace.Id}", new { name = "patched" }, Ct));
    }

    [Fact]
    public async Task PartialEdit_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, _, workspace) = await SeedWorkspace();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PatchAsJsonAsync($"api/workspaces/{workspace.Id}", new { name = "patched" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(workspace.Name, (await StoredWorkspace(workspace.Id)).Name);
    }

    [Fact]
    public async Task PartialEdit_is_forbidden_for_a_caller_holding_only_ViewProjects()
    {
        var (_, _, workspace) = await SeedWorkspace();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var response = await Client(actor).PatchAsJsonAsync($"api/workspaces/{workspace.Id}", new { name = "patched" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(workspace.Name, (await StoredWorkspace(workspace.Id)).Name);
    }

    [Fact]
    public async Task PartialEdit_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, _, workspace) = await SeedWorkspace();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).PatchAsJsonAsync($"api/workspaces/{workspace.Id}", new { name = "patched" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(workspace.Name, (await StoredWorkspace(workspace.Id)).Name);
    }

    /// <summary>A partial edit naming a directory of another project leaves the workspace in its own directory.</summary>
    [Fact]
    public async Task PartialEdit_keeps_the_workspace_in_its_directory_when_the_body_names_a_directory_of_another_project()
    {
        var (project, directory, workspace) = await SeedWorkspace();
        var (_, foreignDirectory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PatchAsJsonAsync($"api/workspaces/{workspace.Id}", new { directoryId = foreignDirectory.Id }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(directory.Id, (await StoredWorkspace(workspace.Id)).DirectoryId);
    }

    // ---- PUT/PATCH api/workspaces/{id}: the stored workspace decides ------------------------------

    /// <summary>
    /// The workspace on the route is authorized, not the workspace or directory the body names, and nothing of
    /// the route's workspace or its files changes.
    /// </summary>
    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    public async Task Edit_and_PartialEdit_authorize_the_route_even_when_the_body_names_an_authorized_directory(string method)
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Send(actor, method, $"api/workspaces/{graph.WorkspaceB.Id}",
            new { id = graph.WorkspaceA.Id, name = "stolen", directoryId = graph.DirectoryA.Id });

        await AssertProblem(HttpStatusCode.Forbidden, response);
        var stored = await StoredWorkspace(graph.WorkspaceB.Id);
        Assert.Equal(graph.DirectoryB.Id, stored.DirectoryId);
        Assert.Equal("workspace-b", stored.Name);
        Assert.Equal(TwoProjectGraph.State, stored.State);
        Assert.Equal(TwoProjectGraph.StateBackup, stored.StateBackup);
        Assert.Equal(graph.DirectoryB.Id, (await StoredFile(graph.FileB.Id)).DirectoryId);
    }

    /// <summary>
    /// A <c>directoryId</c> in the body, of another project's directory, of no directory, null or left out,
    /// is ignored: the workspace is renamed where it is, and keeps its state and its files.
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
    public async Task Edit_and_PartialEdit_ignore_legacy_directory_fields(string method, string parent)
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.EditProject]).SeedAsync();
        var body = WithLegacyField(new Dictionary<string, object> { ["name"] = "renamed" }, "directoryId", parent, graph.DirectoryB.Id);

        var response = await Send(actor, method, $"api/workspaces/{graph.WorkspaceA.Id}", body);

        await AssertStatus(HttpStatusCode.OK, response);
        var stored = await StoredWorkspace(graph.WorkspaceA.Id);
        Assert.Equal("renamed", stored.Name);
        Assert.Equal(graph.DirectoryA.Id, stored.DirectoryId);
        Assert.Equal(TwoProjectGraph.State, stored.State);
        Assert.Equal(TwoProjectGraph.StateBackup, stored.StateBackup);
        Assert.Equal(graph.DirectoryA.Id, (await StoredFile(graph.FileA.Id)).DirectoryId);
    }

    // ---- DELETE api/workspaces/{id} -------------------------------------------------------------

    [Fact]
    public async Task Delete_by_a_member_holding_EditProject_removes_the_workspace()
    {
        var (project, _, workspace) = await SeedWorkspace();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/workspaces/{workspace.Id}", Ct);

        await AssertStatus(HttpStatusCode.NoContent, response);
        Assert.Null(await StoredWorkspace(workspace.Id));
    }

    [Fact]
    public async Task Delete_by_a_caller_holding_EditProjects_removes_the_workspace()
    {
        var (_, _, workspace) = await SeedWorkspace();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.NoContent, await Client(actor).DeleteAsync($"api/workspaces/{workspace.Id}", Ct));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, _, workspace) = await SeedWorkspace();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/workspaces/{workspace.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.NotNull(await StoredWorkspace(workspace.Id));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_holding_only_ViewProjects()
    {
        var (_, _, workspace) = await SeedWorkspace();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/workspaces/{workspace.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.NotNull(await StoredWorkspace(workspace.Id));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, _, workspace) = await SeedWorkspace();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/workspaces/{workspace.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.NotNull(await StoredWorkspace(workspace.Id));
    }

    [Fact]
    public async Task Delete_of_a_workspace_with_a_queued_run_is_a_conflict()
    {
        var (_, _, workspace) = await SeedWorkspace();
        await Seed(TestData.Run(workspace));

        var response = await RootClient.DeleteAsync($"api/workspaces/{workspace.Id}", Ct);

        await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.NotNull(await StoredWorkspace(workspace.Id));
    }

    [Fact]
    public async Task Delete_broadcasts_the_removed_id_to_the_projects_group()
    {
        var (project, _, workspace) = await SeedWorkspace();

        await RootClient.DeleteAsync($"api/workspaces/{workspace.Id}", Ct);

        var broadcast = Assert.Single(Factory.Hub<ProjectHub>().ToGroup(project.Id), x => x.Method == "WorkspaceDeleted");
        Assert.Equal(workspace.Id, broadcast.Arguments[0]);
    }

    // ---- workspace locking ----------------------------------------------------------------------

    [Fact]
    public async Task GetLockingStatus_answers_for_a_caller_holding_ViewWorkspaces()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewWorkspaces).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).GetAsync("api/workspaces/locking-status", Ct));
    }

    [Fact]
    public async Task GetLockingStatus_is_forbidden_for_a_caller_holding_only_ViewProjects()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync("api/workspaces/locking-status", Ct));
    }

    /// <summary>
    /// Enabling is the shipped state, so this leaves the run-wide lock service as every other test expects
    /// it. Disabling is only asserted refused: an allowed disable would change it for tests running alongside.
    /// </summary>
    [Fact]
    public async Task EnableLocking_by_a_caller_holding_ManageWorkspaces_answers_enabled_and_tells_the_admin_group()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageWorkspaces).SeedAsync();

        var response = await Client(actor).PostAsync("api/workspaces/actions/enable-locking", null, Ct);

        Assert.True(await ReadAsync<bool>(response));
        Assert.Contains(Factory.Hub<ProjectHub>().ToGroup(nameof(HubGroups.WorkspacesAdmin)), x => x.Method == "WorkspaceSettingsUpdated");
    }

    [Fact]
    public async Task EnableLocking_is_forbidden_for_a_caller_holding_only_ViewWorkspaces()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewWorkspaces).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsync("api/workspaces/actions/enable-locking", null, Ct));
    }

    [Fact]
    public async Task DisableLocking_is_forbidden_for_a_caller_holding_only_ViewWorkspaces()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewWorkspaces).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsync("api/workspaces/actions/disable-locking", null, Ct));
    }

    // ---- helpers --------------------------------------------------------------------------------

    private Task<TwoProjectGraph> SeedGraph() => TwoProjectGraph.SeedAsync(Db, Root.Id, Ct);

    private Task<HttpResponseMessage> Send(TestActor actor, string method, string url, object body) =>
        Client(actor).SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = JsonContent.Create(body) }, Ct);

    /// <summary>
    /// <paramref name="body"/> with <paramref name="field"/> set as an old client would send it: the id of a row
    /// of another project (<c>foreign</c>), an id nothing has (<c>missing</c>), <c>null</c>, or left out
    /// (<c>omitted</c>).
    /// </summary>
    private static Dictionary<string, object> WithLegacyField(Dictionary<string, object> body, string field, string scenario, Guid foreignId)
    {
        if (scenario != "omitted")
        {
            body[field] = scenario switch { "foreign" => foreignId, "missing" => Guid.NewGuid(), _ => null };
        }

        return body;
    }

    private async Task<Caster.Api.Domain.Models.File> StoredFile(Guid id)
    {
        await using var context = NewContext();

        return await context.Files.IgnoreQueryFilters().SingleAsync(x => x.Id == id, Ct);
    }

    private async Task<(Project Project, Directory Directory)> SeedDirectory()
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        await Seed(project, directory);

        return (project, directory);
    }

    private async Task<(Project Project, Directory Directory, Workspace Workspace)> SeedWorkspace()
    {
        var (project, directory) = await SeedDirectory();
        var workspace = TestData.Workspace(directory);
        await Seed(workspace);

        return (project, directory, workspace);
    }

    private async Task<Workspace> StoredWorkspace(Guid id)
    {
        await using var context = NewContext();

        return await context.Workspaces.SingleOrDefaultAsync(x => x.Id == id, Ct);
    }

    private async Task<bool> WorkspaceNamed(string name)
    {
        await using var context = NewContext();

        return await context.Workspaces.AnyAsync(x => x.Name == name, Ct);
    }
}
