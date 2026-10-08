// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;
using File = System.IO.File;
using ResourceView = Caster.Api.Features.Resources.Resource;

namespace Caster.Api.Tests.Features.Resources;

/// <summary>
/// <c>ResourcesController</c>: a workspace's resources (from its Terraform state) are read with ViewProject
/// (or ViewProjects); taint, untaint, remove and refresh need EditProject (or EditProjects), outputs
/// ViewProject, and import the system permission ImportResources alone.
/// </summary>
/// <remarks>
/// The commands run Terraform once past their gate, and nothing here installs it. The allowed cases
/// therefore seed a queued run on the workspace: the handler then answers 409 for the workspace being busy,
/// which it can only reach after the gate let the caller through.
/// </remarks>
public class ResourceRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    /// <summary>A vSphere virtual machine in <c>Data/terraform.tfstate</c>.</summary>
    private const string MachineAddress = "vsphere_virtual_machine.course-centos7-server";

    /// <summary>The title of the 409 <c>WorkspaceConflictException</c> answers for a workspace with a run in progress.</summary>
    private const string BusyWorkspaceTitle = "Only one operation can be performed on a Workspace at a time";

    // ---- reads ----------------------------------------------------------------------------------

    [Fact]
    public async Task GetByWorkspace_lists_the_resources_in_the_state_for_a_member_holding_ViewProject()
    {
        var (project, workspace) = await SeedWorkspace();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/workspaces/{workspace.Id}/resources", Ct);

        Assert.Equal(13, (await ReadAsync<ResourceView[]>(response)).Length);
    }

    [Fact]
    public async Task GetByWorkspace_lists_the_resources_for_a_caller_holding_ViewProjects()
    {
        var (_, workspace) = await SeedWorkspace();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).GetAsync($"api/workspaces/{workspace.Id}/resources", Ct));
    }

    [Fact]
    public async Task GetByWorkspace_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var (project, workspace) = await SeedWorkspace();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/workspaces/{workspace.Id}/resources", Ct));
    }

    [Fact]
    public async Task GetByWorkspace_is_forbidden_for_a_caller_holding_ViewProject_only_on_another_project()
    {
        var (_, workspace) = await SeedWorkspace();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/workspaces/{workspace.Id}/resources", Ct));
    }

    [Fact]
    public async Task Get_returns_one_resource_by_address_to_a_member_holding_ViewProject()
    {
        var (project, workspace) = await SeedWorkspace();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/workspaces/{workspace.Id}/resources/{MachineAddress}", Ct);

        Assert.Equal("course.centos7.server-4c2eb68c-a77f-45aa-990a-6b837ee59d71", (await ReadAsync<ResourceView>(response)).Name);
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_ViewProject_only_on_another_project()
    {
        var (_, workspace) = await SeedWorkspace();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/workspaces/{workspace.Id}/resources/{MachineAddress}", Ct));
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var (project, workspace) = await SeedWorkspace();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/workspaces/{workspace.Id}/resources/{MachineAddress}", Ct));
    }

    // ---- taint, untaint, remove, refresh: EditProject ------------------------------------------

    [Fact]
    public async Task Taint_by_a_member_holding_EditProject_reaches_the_busy_workspace()
    {
        var (project, workspace) = await SeedBusyWorkspace();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(Url(workspace, "taint"), new { resourceAddresses = new[] { MachineAddress } }, Ct);

        Assert.Equal(BusyWorkspaceTitle, (await AssertProblem(HttpStatusCode.Conflict, response)).Title);
    }

    [Fact]
    public async Task Taint_by_a_caller_holding_EditProjects_reaches_the_busy_workspace()
    {
        var (_, workspace) = await SeedBusyWorkspace();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(Url(workspace, "taint"), new { resourceAddresses = new[] { MachineAddress } }, Ct);

        Assert.Equal(BusyWorkspaceTitle, (await AssertProblem(HttpStatusCode.Conflict, response)).Title);
    }

    [Fact]
    public async Task Taint_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, workspace) = await SeedBusyWorkspace();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(Url(workspace, "taint"), new { resourceAddresses = new[] { MachineAddress } }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Taint_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, workspace) = await SeedBusyWorkspace();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(Url(workspace, "taint"), new { resourceAddresses = new[] { MachineAddress } }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Untaint_by_a_member_holding_EditProject_reaches_the_busy_workspace()
    {
        var (project, workspace) = await SeedBusyWorkspace();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(Url(workspace, "untaint"), new { resourceAddresses = new[] { MachineAddress } }, Ct);

        Assert.Equal(BusyWorkspaceTitle, (await AssertProblem(HttpStatusCode.Conflict, response)).Title);
    }

    [Fact]
    public async Task Untaint_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, workspace) = await SeedBusyWorkspace();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(Url(workspace, "untaint"), new { resourceAddresses = new[] { MachineAddress } }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Untaint_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, workspace) = await SeedBusyWorkspace();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(Url(workspace, "untaint"), new { resourceAddresses = new[] { MachineAddress } }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Remove_by_a_member_holding_EditProject_reaches_the_busy_workspace()
    {
        var (project, workspace) = await SeedBusyWorkspace();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(Url(workspace, "remove"), new { resourceAddresses = new[] { MachineAddress } }, Ct);

        Assert.Equal(BusyWorkspaceTitle, (await AssertProblem(HttpStatusCode.Conflict, response)).Title);
    }

    [Fact]
    public async Task Remove_by_a_caller_holding_EditProjects_reaches_the_busy_workspace()
    {
        var (_, workspace) = await SeedBusyWorkspace();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(Url(workspace, "remove"), new { resourceAddresses = new[] { MachineAddress } }, Ct);

        Assert.Equal(BusyWorkspaceTitle, (await AssertProblem(HttpStatusCode.Conflict, response)).Title);
    }

    [Fact]
    public async Task Remove_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, workspace) = await SeedBusyWorkspace();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(Url(workspace, "remove"), new { resourceAddresses = new[] { MachineAddress } }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Remove_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, workspace) = await SeedBusyWorkspace();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(Url(workspace, "remove"), new { resourceAddresses = new[] { MachineAddress } }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Refresh_by_a_member_holding_EditProject_reaches_the_busy_workspace()
    {
        var (project, workspace) = await SeedBusyWorkspace();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        Assert.Equal(BusyWorkspaceTitle, (await AssertProblem(HttpStatusCode.Conflict, await Client(actor).PostAsync(Url(workspace, "refresh"), null, Ct))).Title);
    }

    [Fact]
    public async Task Refresh_by_a_caller_holding_EditProjects_reaches_the_busy_workspace()
    {
        var (_, workspace) = await SeedBusyWorkspace();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        Assert.Equal(BusyWorkspaceTitle, (await AssertProblem(HttpStatusCode.Conflict, await Client(actor).PostAsync(Url(workspace, "refresh"), null, Ct))).Title);
    }

    [Fact]
    public async Task Refresh_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, workspace) = await SeedBusyWorkspace();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsync(Url(workspace, "refresh"), null, Ct));
    }

    [Fact]
    public async Task Refresh_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, workspace) = await SeedBusyWorkspace();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsync(Url(workspace, "refresh"), null, Ct));
    }

    // ---- outputs: ViewProject; import: ImportResources ------------------------------------------

    [Fact]
    public async Task Output_by_a_member_holding_ViewProject_reaches_the_busy_workspace()
    {
        var (project, workspace) = await SeedBusyWorkspace();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        Assert.Equal(BusyWorkspaceTitle, (await AssertProblem(HttpStatusCode.Conflict, await Client(actor).PostAsync(Url(workspace, "outputs"), null, Ct))).Title);
    }

    [Fact]
    public async Task Output_by_a_caller_holding_ViewProjects_reaches_the_busy_workspace()
    {
        var (_, workspace) = await SeedBusyWorkspace();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        Assert.Equal(BusyWorkspaceTitle, (await AssertProblem(HttpStatusCode.Conflict, await Client(actor).PostAsync(Url(workspace, "outputs"), null, Ct))).Title);
    }

    [Fact]
    public async Task Output_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var (project, workspace) = await SeedBusyWorkspace();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsync(Url(workspace, "outputs"), null, Ct));
    }

    [Fact]
    public async Task Output_is_forbidden_for_a_caller_holding_ViewProject_only_on_another_project()
    {
        var (_, workspace) = await SeedBusyWorkspace();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsync(Url(workspace, "outputs"), null, Ct));
    }

    [Fact]
    public async Task Import_by_a_caller_holding_ImportResources_reaches_the_busy_workspace()
    {
        var (_, workspace) = await SeedBusyWorkspace();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ImportResources).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(Url(workspace, "import"), new { resourceAddress = MachineAddress, resourceId = "vm-1" }, Ct);

        Assert.Equal(BusyWorkspaceTitle, (await AssertProblem(HttpStatusCode.Conflict, response)).Title);
    }

    /// <summary>Import has no project path: even a manager of the workspace's project is refused.</summary>
    [Fact]
    public async Task Import_is_forbidden_for_a_caller_holding_ManageProject_on_the_workspaces_project()
    {
        var (project, workspace) = await SeedBusyWorkspace();
        var actor = await Actor().OnProject(project, roleId: TestData.ProjectRoles.Manager).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(Url(workspace, "import"), new { resourceAddress = MachineAddress, resourceId = "vm-1" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Import_is_forbidden_for_a_caller_holding_only_EditProjects()
    {
        var (_, workspace) = await SeedBusyWorkspace();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(Url(workspace, "import"), new { resourceAddress = MachineAddress, resourceId = "vm-1" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static string Url(Workspace workspace, string action) => $"api/workspaces/{workspace.Id}/resources/actions/{action}";

    /// <summary>A workspace whose state is <c>Data/terraform.tfstate</c>.</summary>
    private async Task<(Project Project, Workspace Workspace)> SeedWorkspace()
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        var workspace = TestData.Workspace(directory);
        workspace.State = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Data", "terraform.tfstate"), Ct);
        await Seed(project, directory, workspace);

        return (project, workspace);
    }

    /// <summary>A workspace with a run still queued, which every resource command refuses with a 409.</summary>
    private async Task<(Project Project, Workspace Workspace)> SeedBusyWorkspace()
    {
        var (project, workspace) = await SeedWorkspace();
        await Seed(TestData.Run(workspace));

        return (project, workspace);
    }
}
