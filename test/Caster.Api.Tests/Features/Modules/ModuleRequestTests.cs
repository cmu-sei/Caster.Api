// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using ModuleView = Caster.Api.Features.Modules.Module;

namespace Caster.Api.Tests.Features.Modules;

/// <summary>
/// <c>ModulesController</c>: modules are read by anyone on some project or holding ViewModules, snippets need
/// ViewModules, and creating, importing from GitLab and deleting need ManageModules.
/// </summary>
public class ModuleRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    /// <summary>A GitLab project id that only <see cref="CreateFromRepository_by_a_caller_holding_ManageModules_stores_the_module_gitlab_describes"/> stubs.</summary>
    private const int GitlabProjectId = 424242;

    [Fact]
    public async Task Get_returns_the_module_to_a_member_of_any_project()
    {
        var (module, _) = await SeedModule();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        var response = await Client(actor).GetAsync($"api/modules/{module.Id}", Ct);

        Assert.Equal(module.Id, (await ReadAsync<ModuleView>(response)).Id);
    }

    [Fact]
    public async Task Get_returns_the_module_to_a_caller_holding_ViewModules()
    {
        var (module, _) = await SeedModule();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewModules).SeedAsync();

        Assert.Equal(module.Name, (await ReadAsync<ModuleView>(await Client(actor).GetAsync($"api/modules/{module.Id}", Ct))).Name);
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_only_ManageModules()
    {
        var (module, _) = await SeedModule();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageModules).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/modules/{module.Id}", Ct));
    }

    /// <summary>
    /// The versions are listed by version number, highest first, and a tag that is no version number last, whatever
    /// order they were imported in: the Designer and the file editor default to the first one.
    /// </summary>
    [Fact]
    public async Task Get_lists_the_versions_highest_first_whatever_order_they_were_imported_in()
    {
        var module = TestData.Module();
        var older = TestData.ModuleVersion(module, "v1.2.0");
        older.DateCreated = TestData.DefaultDateCreated.AddDays(2);
        var newer = TestData.ModuleVersion(module, "v1.10.0");
        newer.DateCreated = TestData.DefaultDateCreated.AddDays(1);
        var branch = TestData.ModuleVersion(module, "main");
        branch.DateCreated = TestData.DefaultDateCreated.AddDays(3);
        await Seed(module, older, newer, branch);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewModules).SeedAsync();

        var response = await Client(actor).GetAsync($"api/modules/{module.Id}", Ct);

        Assert.Equal(["v1.10.0", "v1.2.0", "main"], (await ReadAsync<ModuleView>(response)).Versions.Select(x => x.Name));
    }

    /// <summary>A module id that is not a Guid is answered with a 500.</summary>
    [Fact]
    public async Task Get_with_a_malformed_id_answers_with_a_server_error()
    {
        var problem = await AssertProblem(HttpStatusCode.InternalServerError, await RootClient.GetAsync("api/modules/not-a-guid", Ct));
        Assert.Equal("Unrecognized Guid format.", problem.Detail);
    }

    [Fact]
    public async Task GetAll_lists_the_modules_for_a_member_of_any_project()
    {
        var (module, _) = await SeedModule();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        var response = await Client(actor).GetAsync("api/modules", Ct);

        Assert.Equal([module.Id], (await ReadAsync<ModuleView[]>(response)).Select(x => x.Id));
    }

    [Fact]
    public async Task GetAll_lists_the_modules_for_a_caller_holding_ViewModules()
    {
        var (module, _) = await SeedModule();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewModules).SeedAsync();

        var response = await Client(actor).GetAsync("api/modules", Ct);

        Assert.Equal([module.Id], (await ReadAsync<ModuleView[]>(response)).Select(x => x.Id));
    }

    [Fact]
    public async Task GetAll_lists_the_modules_for_a_member_holding_only_EditProject_on_a_project()
    {
        var (module, _) = await SeedModule();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).GetAsync("api/modules", Ct);

        Assert.Equal([module.Id], (await ReadAsync<ModuleView[]>(response)).Select(x => x.Id));
    }

    // ---- GET api/modules?designId=: the catalog, then the design's project ----------------------

    [Fact]
    public async Task GetAll_for_a_design_lists_its_modules_to_a_member_holding_ViewProject_on_its_project()
    {
        var (project, design, module) = await SeedModuleOfADesign();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/modules?designId={design.Id}&forceUpdate=true", Ct);

        Assert.Equal([module.Id], (await ReadAsync<ModuleView[]>(response)).Select(x => x.Id));
    }

    [Fact]
    public async Task GetAll_for_a_design_lists_its_modules_to_a_caller_holding_ViewModules_and_ViewProjects()
    {
        var (_, design, module) = await SeedModuleOfADesign();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewModules, SystemPermission.ViewProjects).SeedAsync();

        var response = await Client(actor).GetAsync($"api/modules?designId={design.Id}&forceUpdate=true", Ct);

        Assert.Equal([module.Id], (await ReadAsync<ModuleView[]>(response)).Select(x => x.Id));
    }

    [Fact]
    public async Task GetAll_for_a_design_is_forbidden_for_a_member_holding_ViewProject_only_on_another_project()
    {
        var (_, design, _) = await SeedModuleOfADesign();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/modules?designId={design.Id}&forceUpdate=true", Ct));
    }

    [Fact]
    public async Task GetAll_for_a_design_is_forbidden_for_a_member_holding_only_EditProject_on_its_project()
    {
        var (project, design, _) = await SeedModuleOfADesign();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/modules?designId={design.Id}&forceUpdate=true", Ct));
    }

    /// <summary>ViewModules opens the catalog but not a design: the design's project is checked too.</summary>
    [Fact]
    public async Task GetAll_for_a_design_is_forbidden_for_a_caller_holding_only_ViewModules()
    {
        var (_, design, _) = await SeedModuleOfADesign();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewModules).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/modules?designId={design.Id}&forceUpdate=true", Ct));
    }

    /// <summary>ViewProjects opens the design but not the catalog, which needs a project membership or ViewModules.</summary>
    [Fact]
    public async Task GetAll_for_a_design_is_forbidden_for_a_caller_holding_only_ViewProjects()
    {
        var (_, design, _) = await SeedModuleOfADesign();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/modules?designId={design.Id}&forceUpdate=true", Ct));
    }


    [Fact]
    public async Task GetAll_is_forbidden_for_a_caller_holding_only_ManageModules()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageModules).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync("api/modules", Ct));
    }

    /// <summary>Listing the modules with their versions lists each module with no versions.</summary>
    [Fact]
    public async Task GetAll_with_includeVersions_lists_each_module_without_its_versions()
    {
        var (module, _) = await SeedModule();

        var response = await RootClient.GetAsync("api/modules?includeVersions=true", Ct);

        Assert.Null(Assert.Single(await ReadAsync<ModuleView[]>(response), x => x.Id == module.Id).Versions);
    }

    /// <summary>Listing a module's versions is answered with a 500.</summary>
    [Fact]
    public async Task GetVersions_answers_a_member_of_any_project_with_a_server_error()
    {
        var (module, _) = await SeedModule();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        var response = await Client(actor).GetAsync($"api/modules/{module.Id}/versions", Ct);

        var problem = await AssertProblem(HttpStatusCode.InternalServerError, response);

        Assert.StartsWith("The LINQ expression 'dtoModuleVariable => new ModuleVariable", problem.Detail);
    }

    // Same case as GetVersions_answers_a_member_of_any_project_with_a_server_error.
    [Fact]
    public async Task GetVersions_answers_a_caller_holding_ViewModules_with_a_server_error()
    {
        var module = TestData.Module();
        await Seed(module);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewModules).SeedAsync();

        var response = await Client(actor).GetAsync($"api/modules/{module.Id}/versions", Ct);

        var problem = await AssertProblem(HttpStatusCode.InternalServerError, response);

        Assert.StartsWith("The LINQ expression 'dtoModuleVariable => new ModuleVariable", problem.Detail);
    }

    [Fact]
    public async Task GetVersions_is_forbidden_for_a_caller_holding_only_ViewProjects()
    {
        var (module, _) = await SeedModule();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/modules/{module.Id}/versions", Ct));
    }

    /// <summary>Creating a module is answered with a 500 for a caller the gate lets through.</summary>
    [Fact]
    public async Task Create_by_a_caller_holding_ManageModules_answers_with_a_server_error()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageModules).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/modules", new { name = "network", path = "modules/network" }, Ct);

        var problem = await AssertProblem(HttpStatusCode.InternalServerError, response);

        Assert.Matches(@"^Missing type map configuration or unsupported mapping\.[\s\S]*Modules\.Create\+Command -> Caster\.Api\.Domain\.Models\.Module$", problem.Detail);
        await using var context = NewContext();
        Assert.False(await context.Modules.AnyAsync(x => x.Path == "modules/network", Ct));
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_caller_holding_only_ViewModules()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewModules).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/modules", new { name = "network", path = "modules/refused" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        await using var context = NewContext();
        Assert.False(await context.Modules.AnyAsync(x => x.Path == "modules/refused", Ct));
    }

    [Fact]
    public async Task CreateFromRepository_by_a_caller_holding_ManageModules_stores_the_module_gitlab_describes()
    {
        var path = $"terraform-modules/repository-{Guid.NewGuid():N}";
        Factory.OutboundHttp
            .Respond($"{TestConfiguration.GitlabApiUrl}projects/{GitlabProjectId}", Json(
                $$"""{"id":{{GitlabProjectId}},"name":"imported","path_with_namespace":"{{path}}","http_url_to_repo":"https://gitlab.test/imported.git?ref=master"}"""))
            .Respond($"{TestConfiguration.GitlabApiUrl}projects/{GitlabProjectId}/releases", Json("[]"));
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageModules).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/repository/modules", new { id = GitlabProjectId.ToString() }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        await using var context = NewContext();
        Assert.Equal("imported", (await context.Modules.SingleAsync(x => x.Path == path, Ct)).Name);
    }

    [Fact]
    public async Task CreateFromRepository_is_forbidden_for_a_caller_holding_only_ViewModules()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewModules).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/repository/modules", new { id = "1" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task CreateSnippet_by_a_caller_holding_ViewModules_renders_the_module_block()
    {
        var (_, version) = await SeedModule();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewModules).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(
            "api/modules/snippet", new { versionId = version.Id, moduleName = "web", variableValues = Array.Empty<object>() }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Contains("module \"web\"", await response.Content.ReadAsStringAsync(Ct));
    }

    /// <summary>The snippet has no project path: a manager of a project is refused.</summary>
    [Fact]
    public async Task CreateSnippet_is_forbidden_for_a_member_holding_ManageProject_on_a_project()
    {
        var (_, version) = await SeedModule();
        var actor = await Actor().OnNewProject(ProjectPermission.ManageProject).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(
            "api/modules/snippet", new { versionId = version.Id, moduleName = "web", variableValues = Array.Empty<object>() }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Delete_by_a_caller_holding_ManageModules_removes_the_module()
    {
        var (module, _) = await SeedModule();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageModules).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/modules/{module.Id}", Ct);

        await AssertStatus(HttpStatusCode.NoContent, response);
        await using var context = NewContext();
        Assert.False(await context.Modules.AnyAsync(x => x.Id == module.Id, Ct));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_holding_only_ViewModules()
    {
        var (module, _) = await SeedModule();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewModules).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/modules/{module.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        await using var context = NewContext();
        Assert.True(await context.Modules.AnyAsync(x => x.Id == module.Id, Ct));
    }

    private static byte[] Json(string json) => Encoding.UTF8.GetBytes(json);

    /// <summary>A module used by one design, of a new project; and a module no design uses.</summary>
    private async Task<(Project Project, Design Design, Module Module)> SeedModuleOfADesign()
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        var design = TestData.Design(directory);
        var (module, _) = await SeedModule();
        await Seed(project, directory, design, TestData.DesignModule(design, module), TestData.Module("unused-module"));

        return (project, design, module);
    }

    private async Task<(Module Module, ModuleVersion Version)> SeedModule()
    {
        var module = TestData.Module();
        var version = TestData.ModuleVersion(module);
        await Seed(module, version);

        return (module, version);
    }
}
