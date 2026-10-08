// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using DesignModuleView = Caster.Api.Features.DesignModules.DesignModule;

namespace Caster.Api.Tests.Features.DesignModules;

/// <summary>
/// <c>DesignModulesController</c>: a design's modules are read with ViewProject (or ViewProjects) and changed
/// with EditProject (or EditProjects) on the project the design's directory is in.
/// </summary>
public class DesignModuleRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    /// <summary>An empty values list: the create and edit bodies carry one, and a missing one fails (see below).</summary>
    private static readonly object[] NoValues = [];

    [Fact]
    public async Task Get_returns_the_design_module_to_a_member_holding_ViewProject()
    {
        var (project, _, designModule) = await SeedDesignModule();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/designModules/{designModule.Id}", Ct);

        Assert.Equal(designModule.Name, (await ReadAsync<DesignModuleView>(response)).Name);
    }

    [Fact]
    public async Task Get_returns_the_design_module_to_a_caller_holding_ViewProjects()
    {
        var (_, _, designModule) = await SeedDesignModule();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).GetAsync($"api/designModules/{designModule.Id}", Ct));
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var (project, _, designModule) = await SeedDesignModule();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/designModules/{designModule.Id}", Ct));
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_ViewProject_only_on_another_project()
    {
        var (_, _, designModule) = await SeedDesignModule();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/designModules/{designModule.Id}", Ct));
    }

    [Fact]
    public async Task GetByDesign_lists_the_modules_for_a_member_holding_ViewProject()
    {
        var (project, design, designModule) = await SeedDesignModule();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/designs/{design.Id}/modules", Ct);

        Assert.Equal([designModule.Id], (await ReadAsync<DesignModuleView[]>(response)).Select(x => x.Id));
    }

    [Fact]
    public async Task GetByDesign_is_forbidden_for_a_caller_holding_ViewProject_only_on_another_project()
    {
        var (_, design, _) = await SeedDesignModule();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/designs/{design.Id}/modules", Ct));
    }

    [Fact]
    public async Task GetByDesign_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var (project, design, _) = await SeedDesignModule();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/designs/{design.Id}/modules", Ct));
    }

    [Fact]
    public async Task Create_by_a_member_holding_EditProject_adds_the_module_to_the_design()
    {
        var (project, design) = await SeedDesign();
        var module = await SeedModule();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(
            "api/designModules", new { designId = design.Id, moduleId = module.Id, name = "web", moduleVersion = "1.0.0", values = NoValues }, Ct);

        var created = await ReadAsync<DesignModuleView>(response);
        Assert.Equal(design.Id, (await StoredDesignModule(created.Id)).DesignId);
    }

    [Fact]
    public async Task Create_by_a_caller_holding_EditProjects_adds_the_module()
    {
        var (_, design) = await SeedDesign();
        var module = await SeedModule();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(
            "api/designModules", new { designId = design.Id, moduleId = module.Id, name = "web", moduleVersion = "1.0.0", values = NoValues }, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, design) = await SeedDesign();
        var module = await SeedModule();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(
            "api/designModules", new { designId = design.Id, moduleId = module.Id, name = "web", moduleVersion = "1.0.0", values = NoValues }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.False(await AnyDesignModule(design.Id));
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, design) = await SeedDesign();
        var module = await SeedModule();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(
            "api/designModules", new { designId = design.Id, moduleId = module.Id, name = "web", moduleVersion = "1.0.0", values = NoValues }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.False(await AnyDesignModule(design.Id));
    }

    /// <summary>A create body without <c>values</c> is answered with a 500.</summary>
    [Fact]
    public async Task Create_without_values_answers_with_a_server_error()
    {
        var (_, design) = await SeedDesign();
        var module = await SeedModule();

        var response = await RootClient.PostAsJsonAsync(
            "api/designModules", new { designId = design.Id, moduleId = module.Id, name = "web", moduleVersion = "1.0.0" }, Ct);

        var problem = await AssertProblem(HttpStatusCode.InternalServerError, response);

        Assert.Matches(@"^Error mapping types\.[\s\S]*DesignModules\.Create\+Command -> Caster\.Api\.Domain\.Models\.DesignModule[\s\S]*Destination Member:\s+Values\s*$", problem.Detail);
        Assert.False(await AnyDesignModule(design.Id));
    }

    [Fact]
    public async Task Edit_by_a_member_holding_EditProject_stores_the_new_version()
    {
        var (project, design, designModule) = await SeedDesignModule();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/designModules/{designModule.Id}",
            new { designId = design.Id, moduleId = designModule.ModuleId, name = designModule.Name, moduleVersion = "2.0.0", values = NoValues }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("2.0.0", (await StoredDesignModule(designModule.Id)).ModuleVersion);
    }

    [Fact]
    public async Task Edit_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, design, designModule) = await SeedDesignModule();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/designModules/{designModule.Id}",
            new { designId = design.Id, moduleId = designModule.ModuleId, name = designModule.Name, moduleVersion = "2.0.0", values = NoValues }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal("1.0.0", (await StoredDesignModule(designModule.Id)).ModuleVersion);
    }

    [Fact]
    public async Task Edit_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, design, designModule) = await SeedDesignModule();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/designModules/{designModule.Id}",
            new { designId = design.Id, moduleId = designModule.ModuleId, name = designModule.Name, moduleVersion = "2.0.0", values = NoValues }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal("1.0.0", (await StoredDesignModule(designModule.Id)).ModuleVersion);
    }

    [Fact]
    public async Task Edit_by_a_caller_holding_EditProjects_stores_the_change()
    {
        var (_, design, designModule) = await SeedDesignModule();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/designModules/{designModule.Id}",
            new { designId = design.Id, moduleId = designModule.ModuleId, name = designModule.Name, moduleVersion = "2.0.0", values = NoValues }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
    }

    /// <summary>A member of one project moves a design module into a design of a project it holds nothing on.</summary>
    [Fact]
    public async Task Edit_moves_the_design_module_into_a_design_of_another_project()
    {
        var (project, _, designModule) = await SeedDesignModule();
        var (_, foreignDesign) = await SeedDesign();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/designModules/{designModule.Id}",
            new { designId = foreignDesign.Id, moduleId = designModule.ModuleId, name = designModule.Name, moduleVersion = "1.0.0", values = NoValues }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(foreignDesign.Id, (await StoredDesignModule(designModule.Id)).DesignId);
    }

    [Fact]
    public async Task Disable_by_a_member_holding_EditProject_stores_the_module_disabled()
    {
        var (project, _, designModule) = await SeedDesignModule();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsync($"api/designModules/{designModule.Id}/actions/disable", null, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.False((await StoredDesignModule(designModule.Id)).Enabled);
    }

    [Fact]
    public async Task Enable_by_a_caller_holding_EditProjects_is_accepted()
    {
        var (_, _, designModule) = await SeedDesignModule();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).PostAsync($"api/designModules/{designModule.Id}/actions/enable", null, Ct));
    }

    [Fact]
    public async Task Disable_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, _, designModule) = await SeedDesignModule();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PostAsync($"api/designModules/{designModule.Id}/actions/disable", null, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.True((await StoredDesignModule(designModule.Id)).Enabled);
    }

    [Fact]
    public async Task Disable_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, _, designModule) = await SeedDesignModule();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).PostAsync($"api/designModules/{designModule.Id}/actions/disable", null, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.True((await StoredDesignModule(designModule.Id)).Enabled);
    }

    [Fact]
    public async Task AddOrUpdateValues_by_a_member_holding_EditProject_stores_the_values()
    {
        var (project, _, designModule) = await SeedDesignModule();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(
            $"api/designModules/{designModule.Id}/values", new { values = new[] { new { name = "count", value = "3" } } }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        var stored = await StoredDesignModule(designModule.Id);
        Assert.Equal("3", Assert.Single(stored.Values, x => x.Name == "count").Value);
    }

    [Fact]
    public async Task AddOrUpdateValues_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, _, designModule) = await SeedDesignModule();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(
            $"api/designModules/{designModule.Id}/values", new { values = new[] { new { name = "count", value = "3" } } }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task AddOrUpdateValues_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, _, designModule) = await SeedDesignModule();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(
            $"api/designModules/{designModule.Id}/values", new { values = new[] { new { name = "count", value = "3" } } }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    /// <summary>A values body without <c>values</c> is answered with a 500.</summary>
    // Same case as Create_without_values_answers_with_a_server_error.
    [Fact]
    public async Task AddOrUpdateValues_without_values_answers_with_a_server_error()
    {
        var (_, _, designModule) = await SeedDesignModule();

        var response = await RootClient.PostAsJsonAsync($"api/designModules/{designModule.Id}/values", new { }, Ct);

        var problem = await AssertProblem(HttpStatusCode.InternalServerError, response);

        Assert.Equal("Object reference not set to an instance of an object.", problem.Detail);
    }

    [Fact]
    public async Task Delete_by_a_member_holding_EditProject_removes_the_design_module()
    {
        var (project, _, designModule) = await SeedDesignModule();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/designsModules/{designModule.Id}", Ct);

        await AssertStatus(HttpStatusCode.NoContent, response);
        Assert.Null(await StoredDesignModule(designModule.Id));
    }

    [Fact]
    public async Task Delete_by_a_caller_holding_EditProjects_removes_the_design_module()
    {
        var (_, _, designModule) = await SeedDesignModule();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.NoContent, await Client(actor).DeleteAsync($"api/designsModules/{designModule.Id}", Ct));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, _, designModule) = await SeedDesignModule();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/designsModules/{designModule.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.NotNull(await StoredDesignModule(designModule.Id));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, _, designModule) = await SeedDesignModule();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/designsModules/{designModule.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.NotNull(await StoredDesignModule(designModule.Id));
    }

    private async Task<(Project Project, Design Design)> SeedDesign()
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        var design = TestData.Design(directory);
        await Seed(project, directory, design);

        return (project, design);
    }

    private async Task<Module> SeedModule()
    {
        var module = TestData.Module();
        await Seed(module);

        return module;
    }

    private async Task<(Project Project, Design Design, DesignModule DesignModule)> SeedDesignModule()
    {
        var (project, design) = await SeedDesign();
        var module = await SeedModule();
        var designModule = TestData.DesignModule(design, module);
        await Seed(designModule);

        return (project, design, designModule);
    }

    private async Task<DesignModule> StoredDesignModule(Guid id)
    {
        await using var context = NewContext();

        return await context.DesignModules.SingleOrDefaultAsync(x => x.Id == id, Ct);
    }

    private async Task<bool> AnyDesignModule(Guid designId)
    {
        await using var context = NewContext();

        return await context.DesignModules.AnyAsync(x => x.DesignId == designId, Ct);
    }
}
