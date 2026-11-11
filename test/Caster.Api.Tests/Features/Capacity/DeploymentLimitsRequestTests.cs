// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.Capacity;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using ProjectView = Caster.Api.Features.Projects.Project;
using WorkspaceView = Caster.Api.Features.Workspaces.Workspace;

namespace Caster.Api.Tests.Features.Capacity;

/// <summary>The limits on projects and workspaces, which only a caller with EditProjects sets.</summary>
public class DeploymentLimitsRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    private async Task<(Project Project, Workspace Workspace)> SeedWorkspace()
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        var workspace = TestData.Workspace(directory);
        await Seed(project, directory, workspace);

        return (project, workspace);
    }

    [Fact]
    public async Task An_admin_sets_and_clears_project_and_workspace_limits()
    {
        var (project, workspace) = await SeedWorkspace();
        var admin = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var edited = await ReadAsync<ProjectView>(await Client(admin).PutAsJsonAsync(
            $"api/projects/{project.Id}/limits", new { maxMachines = 20, maxVcpu = 64, maxMemoryGiB = 128, maxDiskGiB = 2000 }, Ct));
        Assert.Equal((20, 64, 128, 2000), (edited.MaxMachines, edited.MaxVcpu, edited.MaxMemoryGiB, edited.MaxDiskGiB));

        var ws = await ReadAsync<WorkspaceView>(await Client(admin).PutAsJsonAsync(
            $"api/workspaces/{workspace.Id}/limits", new { maxVcpu = 32 }, Ct));
        Assert.Equal(32, ws.MaxVcpu);
        Assert.Null(ws.MaxMachines);

        var limits = await ReadAsync<EffectiveLimits>(await RootClient.GetAsync($"api/workspaces/{workspace.Id}/limits", Ct));
        Assert.Equal((32, LimitSource.Workspace), (limits.MaxVcpu.Value, limits.MaxVcpu.Source));
        Assert.Equal((20, LimitSource.Project), (limits.MaxMachines.Value, limits.MaxMachines.Source));

        await AssertStatus(HttpStatusCode.OK, await Client(admin).PutAsJsonAsync($"api/projects/{project.Id}/limits", new { }, Ct));
        await using var context = NewContext();
        Assert.Null((await context.Projects.SingleAsync(x => x.Id == project.Id, Ct)).MaxMachines);
    }

    [Fact]
    public async Task A_project_editor_cannot_set_limits()
    {
        var (project, workspace) = await SeedWorkspace();
        var editor = await Actor().OnProject(project, [ProjectPermission.EditProject, ProjectPermission.ManageProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(editor).PutAsJsonAsync($"api/projects/{project.Id}/limits", new { maxVcpu = 1000 }, Ct));
        await AssertProblem(HttpStatusCode.Forbidden, await Client(editor).PutAsJsonAsync($"api/workspaces/{workspace.Id}/limits", new { maxVcpu = 1000 }, Ct));
    }

    [Fact]
    public async Task A_negative_limit_is_refused()
    {
        var (project, _) = await SeedWorkspace();

        await AssertStatus(HttpStatusCode.BadRequest, await RootClient.PutAsJsonAsync($"api/projects/{project.Id}/limits", new { maxVcpu = -1 }, Ct));
    }
}
