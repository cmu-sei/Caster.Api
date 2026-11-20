// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;

namespace Caster.Api.Tests.Features.ModuleScopes;

/// <summary>
/// POST api/modules/snippet, which the file editor uses to insert a Module: with a project named it is
/// limited to the Modules the project may use, and a project member needs no ViewModules (which would
/// show them every Module). Without a project it is as before: ViewModules, any Module.
/// </summary>
public class ModuleSnippetScopeTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    private record Seeded(Module Inside, ModuleVersion InsideVersion, Module Outside, ModuleVersion OutsideVersion);

    private async Task<Seeded> SeedModules()
    {
        var inside = TestData.Module("inside");
        inside.Path = "sei/inside";
        var outside = TestData.Module("outside");
        outside.Path = "cmu/outside";
        var insideVersion = TestData.ModuleVersion(inside);
        var outsideVersion = TestData.ModuleVersion(outside);
        await Seed(inside, outside, insideVersion, outsideVersion);

        return new Seeded(inside, insideVersion, outside, outsideVersion);
    }

    private async Task<Project> SeedProject(params string[] scopePaths)
    {
        var project = TestData.Project();
        await Seed(project);

        if (scopePaths.Length > 0)
        {
            var scope = TestData.ModuleScope(null, scopePaths);
            await Seed(scope, TestData.ProjectModuleScope(project, scope));
        }

        return project;
    }

    private Task<HttpResponseMessage> Snippet(TestActor actor, ModuleVersion version, Guid? projectId) =>
        Client(actor).PostAsJsonAsync("api/modules/snippet", new { versionId = version.Id, moduleName = "web", variableValues = Array.Empty<object>(), projectId }, Ct);

    private static async Task AssertSnippet(HttpResponseMessage response)
    {
        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Contains("module \"web\"", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// A project id with no project is a 404 on the list, the lookup, the versions and the snippet, even
    /// for a holder of ViewProjects: it is not a project with no scopes, which would be offered every Module.
    /// </summary>
    [Fact]
    public async Task A_project_that_does_not_exist_is_not_found_on_every_module_endpoint()
    {
        var data = await SeedModules();
        await SeedProject("sei");
        var admin = await Actor().WithSystemPermissions(SystemPermission.ViewProjects, SystemPermission.ViewModules).SeedAsync();
        var missing = Guid.NewGuid();

        await AssertProblem(HttpStatusCode.NotFound, await Client(admin).GetAsync($"api/modules?projectId={missing}", Ct));
        await AssertProblem(HttpStatusCode.NotFound, await Client(admin).GetAsync($"api/modules/{data.Inside.Id}?projectId={missing}", Ct));
        await AssertProblem(HttpStatusCode.NotFound, await Client(admin).GetAsync($"api/modules/{data.Inside.Id}/versions?projectId={missing}", Ct));
        await AssertProblem(HttpStatusCode.NotFound, await Snippet(admin, data.OutsideVersion, missing));
    }

    [Fact]
    public async Task Without_a_project_a_holder_of_ViewModules_gets_any_module_as_before()
    {
        var data = await SeedModules();
        await SeedProject("sei");
        var admin = await Actor().WithSystemPermissions(SystemPermission.ViewModules).SeedAsync();

        await AssertSnippet(await Snippet(admin, data.OutsideVersion, null));
        await AssertSnippet(await Snippet(admin, data.InsideVersion, null));
    }

    [Fact]
    public async Task Without_a_project_a_member_without_ViewModules_is_refused_as_before()
    {
        var data = await SeedModules();
        var project = await SeedProject();
        var member = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Snippet(member, data.InsideVersion, null));
    }

    [Fact]
    public async Task A_project_member_without_ViewModules_gets_a_module_the_project_may_use()
    {
        var data = await SeedModules();
        var project = await SeedProject("sei");
        var member = await Actor().OnProject(project, [ProjectPermission.ViewProject, ProjectPermission.EditProject]).SeedAsync();

        await AssertSnippet(await Snippet(member, data.InsideVersion, project.Id));
    }

    [Fact]
    public async Task A_module_outside_the_projects_scopes_is_not_found_like_a_missing_version()
    {
        var data = await SeedModules();
        var project = await SeedProject("sei");
        var member = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var outside = await Snippet(member, data.OutsideVersion, project.Id);
        var missing = await Client(member).PostAsJsonAsync(
            "api/modules/snippet", new { versionId = Guid.NewGuid(), moduleName = "web", variableValues = Array.Empty<object>(), projectId = project.Id }, Ct);

        await AssertProblem(HttpStatusCode.NotFound, outside);
        await AssertProblem(HttpStatusCode.NotFound, missing);
    }

    [Fact]
    public async Task A_design_using_an_outside_module_does_not_open_it_for_a_snippet()
    {
        var data = await SeedModules();
        var project = await SeedProject("sei");
        var directory = TestData.Directory(project);
        var design = TestData.Design(directory);
        await Seed(directory, design, TestData.DesignModule(design, data.Outside));
        var member = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.NotFound, await Snippet(member, data.OutsideVersion, project.Id));
    }

    [Fact]
    public async Task A_project_with_no_scope_allows_every_module()
    {
        var data = await SeedModules();
        var project = await SeedProject();
        var member = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        await AssertSnippet(await Snippet(member, data.OutsideVersion, project.Id));
        await AssertSnippet(await Snippet(member, data.InsideVersion, project.Id));
    }

    [Fact]
    public async Task Someone_who_is_not_a_member_of_the_named_project_is_refused()
    {
        var data = await SeedModules();
        var project = await SeedProject("sei");
        var other = await Actor().OnNewProject(ProjectPermission.ViewProject, ProjectPermission.EditProject).SeedAsync();
        var noProject = await Actor().WithSystemPermissions(SystemPermission.CreateProjects).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Snippet(other, data.InsideVersion, project.Id));
        await AssertProblem(HttpStatusCode.Forbidden, await Snippet(noProject, data.InsideVersion, project.Id));
    }

    [Fact]
    public async Task A_holder_of_ViewModules_who_names_a_project_is_held_to_its_scopes()
    {
        var data = await SeedModules();
        var project = await SeedProject("sei");
        var admin = await Actor().WithSystemPermissions(SystemPermission.ViewModules).SeedAsync();

        await AssertProblem(HttpStatusCode.NotFound, await Snippet(admin, data.OutsideVersion, project.Id));
        await AssertSnippet(await Snippet(admin, data.InsideVersion, project.Id));
    }

    [Fact]
    public async Task A_holder_of_ViewProjects_gets_a_module_the_project_may_use_and_not_another()
    {
        var data = await SeedModules();
        var project = await SeedProject("sei");
        var viewer = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        await AssertSnippet(await Snippet(viewer, data.InsideVersion, project.Id));
        await AssertProblem(HttpStatusCode.NotFound, await Snippet(viewer, data.OutsideVersion, project.Id));
    }
}
