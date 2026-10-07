// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Hubs;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Directory = Caster.Api.Domain.Models.Directory;
using DesignView = Caster.Api.Features.Designs.Design;

namespace Caster.Api.Tests.Features.Designs;

/// <summary>
/// <c>DesignsController</c>: a directory's designs are read with ViewProject (or ViewProjects) and changed
/// with EditProject (or EditProjects) on the directory's project.
/// </summary>
public class DesignRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    [Fact]
    public async Task Get_returns_the_design_to_a_member_holding_ViewProject()
    {
        var (project, _, design) = await SeedDesign();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/designs/{design.Id}", Ct);

        Assert.Equal(design.Name, (await ReadAsync<DesignView>(response)).Name);
    }

    [Fact]
    public async Task Get_returns_the_design_to_a_caller_holding_ViewProjects()
    {
        var (_, _, design) = await SeedDesign();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).GetAsync($"api/designs/{design.Id}", Ct));
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var (project, _, design) = await SeedDesign();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/designs/{design.Id}", Ct));
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_ViewProject_only_on_another_project()
    {
        var (_, _, design) = await SeedDesign();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/designs/{design.Id}", Ct));
    }

    [Fact]
    public async Task GetByDirectory_lists_the_designs_for_a_member_holding_ViewProject()
    {
        var (project, directory, design) = await SeedDesign();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/directories/{directory.Id}/designs", Ct);

        Assert.Equal([design.Id], (await ReadAsync<DesignView[]>(response)).Select(x => x.Id));
    }

    [Fact]
    public async Task GetByDirectory_lists_the_designs_for_a_caller_holding_ViewProjects()
    {
        var (_, directory, _) = await SeedDesign();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).GetAsync($"api/directories/{directory.Id}/designs", Ct));
    }

    [Fact]
    public async Task GetByDirectory_is_forbidden_for_a_caller_holding_ViewProject_only_on_another_project()
    {
        var (_, directory, _) = await SeedDesign();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/directories/{directory.Id}/designs", Ct));
    }

    [Fact]
    public async Task GetByDirectory_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var (project, directory, _) = await SeedDesign();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/directories/{directory.Id}/designs", Ct));
    }

    [Fact]
    public async Task Create_by_a_member_holding_EditProject_stores_the_design()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/designs", new { name = "created", directoryId = directory.Id }, Ct);

        var created = await ReadAsync<DesignView>(response);
        var stored = await StoredDesign(created.Id);
        Assert.Equal(directory.Id, stored.DirectoryId);
        Assert.True(stored.Enabled);
    }

    [Fact]
    public async Task Create_by_a_caller_holding_EditProjects_stores_the_design()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.Created, await Client(actor).PostAsJsonAsync("api/designs", new { name = "created", directoryId = directory.Id }, Ct));
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, directory) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/designs", new { name = "refused", directoryId = directory.Id }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.False(await DesignNamed("refused"));
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, directory) = await SeedDirectory();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/designs", new { name = "refused", directoryId = directory.Id }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.False(await DesignNamed("refused"));
    }

    [Fact]
    public async Task Create_broadcasts_the_design_to_the_projects_group()
    {
        var (project, directory) = await SeedDirectory();

        var response = await RootClient.PostAsJsonAsync("api/designs", new { name = "announced", directoryId = directory.Id }, Ct);

        var created = await ReadAsync<DesignView>(response);
        var broadcast = Assert.Single(Factory.Hub<ProjectHub>().ToGroup(project.Id), x => x.Method == ProjectHubMethods.DesignCreated);
        Assert.Equal(created.Id, Assert.IsType<DesignView>(broadcast.Arguments[0]).Id);
    }

    [Fact]
    public async Task Edit_by_a_member_holding_EditProject_stores_the_new_name()
    {
        var (project, _, design) = await SeedDesign();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/designs/{design.Id}", new { name = "renamed" }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("renamed", (await StoredDesign(design.Id)).Name);
    }

    [Fact]
    public async Task Edit_by_a_caller_holding_EditProjects_stores_the_new_name()
    {
        var (_, _, design) = await SeedDesign();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).PutAsJsonAsync($"api/designs/{design.Id}", new { name = "renamed" }, Ct));
    }

    [Fact]
    public async Task Edit_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, _, design) = await SeedDesign();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/designs/{design.Id}", new { name = "renamed" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(design.Name, (await StoredDesign(design.Id)).Name);
    }

    [Fact]
    public async Task Edit_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, _, design) = await SeedDesign();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/designs/{design.Id}", new { name = "renamed" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(design.Name, (await StoredDesign(design.Id)).Name);
    }

    [Fact]
    public async Task Disable_by_a_member_holding_EditProject_stores_the_design_disabled()
    {
        var (project, _, design) = await SeedDesign();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsync($"api/designs/{design.Id}/actions/disable", null, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.False((await StoredDesign(design.Id)).Enabled);
    }

    [Fact]
    public async Task Enable_by_a_caller_holding_EditProjects_stores_the_design_enabled()
    {
        var (_, _, design) = await SeedDesign(enabled: false);
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        await Client(actor).PostAsync($"api/designs/{design.Id}/actions/enable", null, Ct);

        Assert.True((await StoredDesign(design.Id)).Enabled);
    }

    [Fact]
    public async Task Disable_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, _, design) = await SeedDesign();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PostAsync($"api/designs/{design.Id}/actions/disable", null, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.True((await StoredDesign(design.Id)).Enabled);
    }

    [Fact]
    public async Task Enable_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, _, design) = await SeedDesign(enabled: false);
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).PostAsync($"api/designs/{design.Id}/actions/enable", null, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.False((await StoredDesign(design.Id)).Enabled);
    }

    [Fact]
    public async Task Delete_by_a_member_holding_EditProject_removes_the_design()
    {
        var (project, _, design) = await SeedDesign();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/designs/{design.Id}", Ct);

        await AssertStatus(HttpStatusCode.NoContent, response);
        Assert.Null(await StoredDesign(design.Id));
    }

    [Fact]
    public async Task Delete_by_a_caller_holding_EditProjects_removes_the_design()
    {
        var (_, _, design) = await SeedDesign();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.NoContent, await Client(actor).DeleteAsync($"api/designs/{design.Id}", Ct));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, _, design) = await SeedDesign();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/designs/{design.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.NotNull(await StoredDesign(design.Id));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, _, design) = await SeedDesign();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/designs/{design.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.NotNull(await StoredDesign(design.Id));
    }

    private async Task<(Project Project, Directory Directory)> SeedDirectory()
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        await Seed(project, directory);

        return (project, directory);
    }

    private async Task<(Project Project, Directory Directory, Design Design)> SeedDesign(bool enabled = true)
    {
        var (project, directory) = await SeedDirectory();
        var design = TestData.Design(directory);
        design.Enabled = enabled;
        await Seed(design);

        return (project, directory, design);
    }

    private async Task<Design> StoredDesign(Guid id)
    {
        await using var context = NewContext();

        return await context.Designs.SingleOrDefaultAsync(x => x.Id == id, Ct);
    }

    private async Task<bool> DesignNamed(string name)
    {
        await using var context = NewContext();

        return await context.Designs.AnyAsync(x => x.Name == name, Ct);
    }
}
