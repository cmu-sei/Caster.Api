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

    /// <summary>A module id that is not a Guid is answered with a 500.</summary>
    [Fact]
    public async Task Get_with_a_malformed_id_answers_with_a_server_error()
    {
        await AssertProblem(HttpStatusCode.InternalServerError, await RootClient.GetAsync("api/modules/not-a-guid", Ct));
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
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewModules).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).GetAsync("api/modules", Ct));
    }

    [Fact]
    public async Task GetAll_is_forbidden_for_a_caller_holding_only_ManageModules()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageModules).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync("api/modules", Ct));
    }

    /// <summary>Listing a module's versions is answered with a 500.</summary>
    [Fact]
    public async Task GetVersions_answers_a_member_of_any_project_with_a_server_error()
    {
        var (module, _) = await SeedModule();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        var response = await Client(actor).GetAsync($"api/modules/{module.Id}/versions", Ct);

        await AssertProblem(HttpStatusCode.InternalServerError, response);
    }

    [Fact]
    public async Task GetVersions_answers_a_caller_holding_ViewModules_with_a_server_error()
    {
        // Same case as GetVersions_answers_a_member_of_any_project_with_a_server_error.
        var module = TestData.Module();
        await Seed(module);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewModules).SeedAsync();

        var response = await Client(actor).GetAsync($"api/modules/{module.Id}/versions", Ct);

        await AssertProblem(HttpStatusCode.InternalServerError, response);
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

        await AssertProblem(HttpStatusCode.InternalServerError, response);
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
            .Respond($"{TestConfiguration.GitlabApiUrl}projects/{GitlabProjectId}?private_token=", Json(
                $$"""{"id":{{GitlabProjectId}},"name":"imported","path_with_namespace":"{{path}}","http_url_to_repo":"https://gitlab.test/imported.git?ref=master"}"""))
            .Respond($"{TestConfiguration.GitlabApiUrl}projects/{GitlabProjectId}/releases?private_token=", Json("[]"));
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

    private async Task<(Module Module, ModuleVersion Version)> SeedModule()
    {
        var module = TestData.Module();
        var version = TestData.ModuleVersion(module);
        await Seed(module, version);

        return (module, version);
    }
}
