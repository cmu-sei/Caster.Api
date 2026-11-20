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
using ModuleView = Caster.Api.Features.Modules.Module;

namespace Caster.Api.Tests.Features.ModuleScopes;

/// <summary>
/// What a project's Module Scopes limit, enforced by the API: the module list, a module and its versions
/// looked up, and the Modules a Design can be given. No scope means every Module; the Modules admin page
/// (no project, ViewModules) is never limited; a Design that already uses an outside Module keeps it.
/// </summary>
public class ModuleScopeEnforcementTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    private static readonly object[] NoValues = [];

    private record Catalog(Module CmuX, Module CmuXDeep, Module CmuXy, Module CmuY, Module Sei);

    /// <summary>Modules at cmu/x, cmu/x/deep, cmu/xy, cmu/y and sei/c, each with a version 1.0.0.</summary>
    private async Task<Catalog> SeedCatalog()
    {
        Module Make(string name, string path)
        {
            var module = TestData.Module(name);
            module.Path = path;
            return module;
        }

        var catalog = new Catalog(Make("x", "cmu/x"), Make("deep", "cmu/x/deep"), Make("xy", "cmu/xy"), Make("y", "cmu/y"), Make("c", "sei/c"));
        Module[] all = [catalog.CmuX, catalog.CmuXDeep, catalog.CmuXy, catalog.CmuY, catalog.Sei];
        await Seed(all.Cast<object>().Concat(all.Select(x => (object)TestData.ModuleVersion(x))).ToArray());

        return catalog;
    }

    private async Task<Project> SeedProjectScopedTo(params string[] paths)
    {
        var project = TestData.Project();
        var scope = TestData.ModuleScope(null, paths);
        await Seed(project, scope, TestData.ProjectModuleScope(project, scope));

        return project;
    }

    private async Task<Guid[]> ListIds(TestActor actor, string query = "")
    {
        var response = await Client(actor).GetAsync($"api/modules{query}", Ct);
        return (await ReadAsync<ModuleView[]>(response)).Select(x => x.Id).Order().ToArray();
    }

    /// <summary>
    /// Listing a module's versions answers with a 500 whatever the scopes (the known defect
    /// ModuleRequestTests pins), so a version request that passes the gate is told apart from one
    /// the scopes stop by its not being a 403 or 404.
    /// </summary>
    private static void AssertPassedTheScopeGate(System.Net.Http.HttpResponseMessage response)
    {
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static Guid[] Ids(params Module[] modules) => modules.Select(x => x.Id).Order().ToArray();

    // ---- GET api/modules -------------------------------------------------------------------------

    [Fact]
    public async Task GetAll_lists_every_module_when_the_project_has_no_scope()
    {
        var catalog = await SeedCatalog();
        var project = TestData.Project();
        await Seed(project);
        var member = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        Assert.Equal(Ids(catalog.CmuX, catalog.CmuXDeep, catalog.CmuXy, catalog.CmuY, catalog.Sei), await ListIds(member, $"?projectId={project.Id}"));
        Assert.Equal(5, (await ListIds(member)).Length);
    }

    [Fact]
    public async Task GetAll_for_a_project_lists_only_the_modules_at_or_below_its_paths()
    {
        var catalog = await SeedCatalog();
        var project = await SeedProjectScopedTo("cmu/x");
        var member = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        // cmu/xy and cmu/y are not below cmu/x
        Assert.Equal(Ids(catalog.CmuX, catalog.CmuXDeep), await ListIds(member, $"?projectId={project.Id}"));
    }

    [Fact]
    public async Task GetAll_lists_the_union_of_the_scopes_of_the_project_and_of_its_group()
    {
        var catalog = await SeedCatalog();
        var group = new ProjectGroup { Id = Guid.NewGuid(), Name = "group" };
        var project = TestData.Project();
        project.ProjectGroupId = group.Id;
        var own = TestData.ModuleScope("Own", "sei");
        var shared = TestData.ModuleScope("Shared", "cmu/y");
        await Seed(group, project, own, shared, TestData.ProjectModuleScope(project, own), TestData.ProjectGroupModuleScope(group, shared));
        var member = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        Assert.Equal(Ids(catalog.CmuY, catalog.Sei), await ListIds(member, $"?projectId={project.Id}"));
    }

    /// <summary>A client that leaves the project out still gets only what its projects allow.</summary>
    [Fact]
    public async Task GetAll_without_a_project_limits_a_member_to_what_their_projects_allow()
    {
        var catalog = await SeedCatalog();
        var cmu = await SeedProjectScopedTo("cmu/y");
        var sei = await SeedProjectScopedTo("sei");
        var member = await Actor()
            .OnProject(cmu, [ProjectPermission.ViewProject])
            .OnProject(sei, [ProjectPermission.ViewProject])
            .SeedAsync();

        Assert.Equal(Ids(catalog.CmuY, catalog.Sei), await ListIds(member));
        Assert.Equal(Ids(catalog.CmuY), await ListIds(member, $"?projectId={cmu.Id}"));
    }

    [Fact]
    public async Task GetAll_without_a_project_gives_every_module_to_a_member_of_an_unrestricted_project()
    {
        await SeedCatalog();
        var restricted = await SeedProjectScopedTo("sei");
        var open = TestData.Project();
        await Seed(open);
        var member = await Actor()
            .OnProject(restricted, [ProjectPermission.ViewProject])
            .OnProject(open, [ProjectPermission.ViewProject])
            .SeedAsync();

        Assert.Equal(5, (await ListIds(member)).Length);
        Assert.Single(await ListIds(member, $"?projectId={restricted.Id}"));
    }

    [Fact]
    public async Task GetAll_without_a_project_is_never_limited_for_a_holder_of_ViewModules()
    {
        await SeedCatalog();
        var project = await SeedProjectScopedTo("sei");
        var admin = await Actor().WithSystemPermissions(SystemPermission.ViewModules, SystemPermission.ViewProjects).SeedAsync();

        Assert.Equal(5, (await ListIds(admin)).Length);
        // naming the project asks for that project's view, for anyone
        Assert.Single(await ListIds(admin, $"?projectId={project.Id}"));
    }

    [Fact]
    public async Task GetAll_for_a_project_needs_ViewProject_on_it_or_ViewProjects()
    {
        await SeedCatalog();
        var project = await SeedProjectScopedTo("sei");
        var other = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();
        var editor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        var catalogOnly = await Actor().WithSystemPermissions(SystemPermission.ViewModules).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(other).GetAsync($"api/modules?projectId={project.Id}", Ct));
        await AssertProblem(HttpStatusCode.Forbidden, await Client(editor).GetAsync($"api/modules?projectId={project.Id}", Ct));
        await AssertProblem(HttpStatusCode.Forbidden, await Client(catalogOnly).GetAsync($"api/modules?projectId={project.Id}", Ct));
    }

    [Fact]
    public async Task GetAll_for_a_design_lists_the_modules_it_uses_even_outside_the_projects_scopes()
    {
        var catalog = await SeedCatalog();
        var project = await SeedProjectScopedTo("sei");
        var directory = TestData.Directory(project);
        var design = TestData.Design(directory);
        await Seed(directory, design, TestData.DesignModule(design, catalog.CmuY));
        var member = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        Assert.Equal(Ids(catalog.CmuY), await ListIds(member, $"?designId={design.Id}&projectId={project.Id}"));
        Assert.Equal(Ids(catalog.Sei), await ListIds(member, $"?projectId={project.Id}"));
    }

    [Fact]
    public async Task A_change_to_a_scope_applies_to_the_next_request()
    {
        var catalog = await SeedCatalog();
        var project = TestData.Project();
        var scope = TestData.ModuleScope("Scope", "sei");
        await Seed(project, scope, TestData.ProjectModuleScope(project, scope));
        var member = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();
        Assert.Single(await ListIds(member, $"?projectId={project.Id}"));

        await AssertStatus(HttpStatusCode.OK, await RootClient.PutAsJsonAsync($"api/module-scopes/{scope.Id}", new { name = "Scope", paths = new[] { "cmu" } }, Ct));

        Assert.Equal(Ids(catalog.CmuX, catalog.CmuXDeep, catalog.CmuXy, catalog.CmuY), await ListIds(member, $"?projectId={project.Id}"));
    }

    // ---- GET api/modules/{id} and its versions --------------------------------------------------

    [Fact]
    public async Task Get_and_versions_of_a_module_outside_the_scopes_are_not_found_to_a_member()
    {
        var catalog = await SeedCatalog();
        var project = await SeedProjectScopedTo("sei");
        var member = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        // with the project named, and without it: the only project the member has is restricted
        foreach (var query in new[] { $"?projectId={project.Id}", "" })
        {
            await AssertProblem(HttpStatusCode.NotFound, await Client(member).GetAsync($"api/modules/{catalog.CmuY.Id}{query}", Ct));
            await AssertProblem(HttpStatusCode.NotFound, await Client(member).GetAsync($"api/modules/{catalog.CmuY.Id}/versions{query}", Ct));
            Assert.Equal(catalog.Sei.Id, (await ReadAsync<ModuleView>(await Client(member).GetAsync($"api/modules/{catalog.Sei.Id}{query}", Ct))).Id);
            AssertPassedTheScopeGate(await Client(member).GetAsync($"api/modules/{catalog.Sei.Id}/versions{query}", Ct));
        }
    }

    [Fact]
    public async Task Get_and_versions_of_a_module_outside_the_scopes_still_work_when_a_design_of_the_project_uses_it()
    {
        var catalog = await SeedCatalog();
        var project = await SeedProjectScopedTo("sei");
        var directory = TestData.Directory(project);
        var design = TestData.Design(directory);
        await Seed(directory, design, TestData.DesignModule(design, catalog.CmuY));
        var member = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(member).GetAsync($"api/modules/{catalog.CmuY.Id}?projectId={project.Id}", Ct));
        AssertPassedTheScopeGate(await Client(member).GetAsync($"api/modules/{catalog.CmuY.Id}/versions?projectId={project.Id}", Ct));
        // but not the other outside modules, and not for a project that doesn't use it
        await AssertProblem(HttpStatusCode.NotFound, await Client(member).GetAsync($"api/modules/{catalog.CmuX.Id}?projectId={project.Id}", Ct));
    }

    [Fact]
    public async Task A_module_used_by_another_projects_design_does_not_open_it_for_this_project()
    {
        var catalog = await SeedCatalog();
        var mine = await SeedProjectScopedTo("sei");
        var theirs = TestData.Project();
        var directory = TestData.Directory(theirs);
        var design = TestData.Design(directory);
        await Seed(theirs, directory, design, TestData.DesignModule(design, catalog.CmuY));
        var member = await Actor().OnProject(mine, [ProjectPermission.ViewProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.NotFound, await Client(member).GetAsync($"api/modules/{catalog.CmuY.Id}?projectId={mine.Id}", Ct));
    }

    [Fact]
    public async Task Get_and_versions_are_not_limited_for_a_holder_of_ViewModules_who_names_no_project()
    {
        var catalog = await SeedCatalog();
        await SeedProjectScopedTo("sei");
        var admin = await Actor().WithSystemPermissions(SystemPermission.ViewModules).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(admin).GetAsync($"api/modules/{catalog.CmuY.Id}", Ct));
        AssertPassedTheScopeGate(await Client(admin).GetAsync($"api/modules/{catalog.CmuY.Id}/versions", Ct));
    }

    [Fact]
    public async Task Get_for_a_project_needs_ViewProject_on_it()
    {
        var catalog = await SeedCatalog();
        var project = await SeedProjectScopedTo("sei");
        var other = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(other).GetAsync($"api/modules/{catalog.Sei.Id}?projectId={project.Id}", Ct));
        await AssertProblem(HttpStatusCode.Forbidden, await Client(other).GetAsync($"api/modules/{catalog.Sei.Id}/versions?projectId={project.Id}", Ct));
    }

    // ---- DesignModules ---------------------------------------------------------------------------

    private async Task<(Project Project, Design Design)> SeedDesign(Project project)
    {
        var directory = TestData.Directory(project);
        var design = TestData.Design(directory);
        await Seed(directory, design);

        return (project, design);
    }

    [Fact]
    public async Task Creating_a_design_module_outside_the_projects_scopes_is_refused_and_inside_is_allowed()
    {
        var catalog = await SeedCatalog();
        var (project, design) = await SeedDesign(await SeedProjectScopedTo("sei"));
        var editor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var outside = await Client(editor).PostAsJsonAsync(
            "api/designModules", new { designId = design.Id, moduleId = catalog.CmuY.Id, name = "web", moduleVersion = "1.0.0", values = NoValues }, Ct);
        var inside = await Client(editor).PostAsJsonAsync(
            "api/designModules", new { designId = design.Id, moduleId = catalog.Sei.Id, name = "web", moduleVersion = "1.0.0", values = NoValues }, Ct);

        await AssertProblem(HttpStatusCode.BadRequest, outside);
        Assert.Contains("Module Scopes", await outside.Content.ReadAsStringAsync(Ct));
        await AssertStatus(HttpStatusCode.Created, inside);
        Assert.Equal(catalog.Sei.Id, Assert.Single(await ReadBack(db => db.DesignModules.Where(x => x.DesignId == design.Id).ToListAsync(Ct))).ModuleId);
    }

    /// <summary>A system administrator editing a project's Designs is held to the project's scopes too.</summary>
    [Fact]
    public async Task Creating_a_design_module_outside_the_scopes_is_refused_even_for_an_administrator()
    {
        var catalog = await SeedCatalog();
        var (_, design) = await SeedDesign(await SeedProjectScopedTo("sei"));

        var response = await RootClient.PostAsJsonAsync(
            "api/designModules", new { designId = design.Id, moduleId = catalog.CmuY.Id, name = "web", moduleVersion = "1.0.0", values = NoValues }, Ct);

        await AssertProblem(HttpStatusCode.BadRequest, response);
    }

    [Fact]
    public async Task Creating_a_design_module_is_unchanged_for_a_project_with_no_scope()
    {
        var catalog = await SeedCatalog();
        var project = TestData.Project();
        await Seed(project);
        var (_, design) = await SeedDesign(project);

        var response = await RootClient.PostAsJsonAsync(
            "api/designModules", new { designId = design.Id, moduleId = catalog.CmuY.Id, name = "web", moduleVersion = "1.0.0", values = NoValues }, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
    }

    [Fact]
    public async Task Editing_a_design_module_keeps_an_outside_module_but_cannot_switch_to_another_outside_one()
    {
        var catalog = await SeedCatalog();
        var (project, design) = await SeedDesign(await SeedProjectScopedTo("sei"));
        var designModule = TestData.DesignModule(design, catalog.CmuY);
        await Seed(designModule);
        var editor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var keep = await Client(editor).PutAsJsonAsync($"api/designModules/{designModule.Id}",
            new { designId = design.Id, moduleId = catalog.CmuY.Id, name = "renamed", moduleVersion = "1.0.0", values = NoValues }, Ct);
        var switchOutside = await Client(editor).PutAsJsonAsync($"api/designModules/{designModule.Id}",
            new { designId = design.Id, moduleId = catalog.CmuX.Id, name = "renamed", moduleVersion = "1.0.0", values = NoValues }, Ct);
        var switchInside = await Client(editor).PutAsJsonAsync($"api/designModules/{designModule.Id}",
            new { designId = design.Id, moduleId = catalog.Sei.Id, name = "renamed", moduleVersion = "1.0.0", values = NoValues }, Ct);

        await AssertStatus(HttpStatusCode.OK, keep);
        await AssertProblem(HttpStatusCode.BadRequest, switchOutside);
        await AssertStatus(HttpStatusCode.OK, switchInside);
    }
}
