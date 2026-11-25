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
using AccessView = Caster.Api.Features.ModuleScopes.ProjectModuleAccess;
using ScopeView = Caster.Api.Features.ModuleScopes.ModuleScope;

namespace Caster.Api.Tests.Features.ModuleScopes;

/// <summary>
/// <c>api/module-scopes</c>, <c>api/projects/{id}/module-scopes</c> and the scope changes of <c>api/assignments</c>:
/// scopes are read with ViewModules (or ManageModules) and made, changed, deleted and assigned with ManageModules; a project's
/// members read what applies to the project, directly and through its group.
/// </summary>
public class ModuleScopeRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    private const string Scopes = "api/module-scopes";
    private const string Assignments = "api/assignments";

    private static ProjectGroup Group() => new() { Id = Guid.NewGuid(), Name = $"group-{Guid.NewGuid():N}" };

    // ---- create, edit, delete --------------------------------------------------------------------

    [Fact]
    public async Task Create_stores_the_trimmed_paths_without_repeats()
    {
        var response = await RootClient.PostAsJsonAsync(Scopes, new
        {
            name = "  CMU exercise X ",
            description = "one folder",
            paths = new[] { "cmu/exercise-x/", " cmu/exercise-x", "sei" }
        }, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        var view = await ReadAsync<ScopeView>(response);
        Assert.Equal("CMU exercise X", view.Name);
        Assert.Equal(["cmu/exercise-x", "sei"], view.Paths);
        Assert.Equal(["cmu/exercise-x", "sei"], (await ReadBack(db => db.ModuleScopes.SingleAsync(x => x.Id == view.Id, Ct))).Paths.Order());
    }

    [Fact]
    public async Task Create_needs_ManageModules()
    {
        var viewer = await Actor().WithSystemPermissions(SystemPermission.ViewModules).SeedAsync();
        var manager = await Actor().WithSystemPermissions(SystemPermission.ManageModules).SeedAsync();
        var body = new { name = "scope", paths = new[] { "cmu" } };

        await AssertProblem(HttpStatusCode.Forbidden, await Client(viewer).PostAsJsonAsync(Scopes, body, Ct));
        await AssertStatus(HttpStatusCode.Created, await Client(manager).PostAsJsonAsync(Scopes, body, Ct));
    }

    [Theory]
    [InlineData("cmu/*")]
    [InlineData("")]
    [InlineData("../cmu")]
    public async Task Create_refuses_a_path_that_is_not_a_source_or_folder(string path)
    {
        var response = await RootClient.PostAsJsonAsync(Scopes, new { name = "scope", paths = new[] { "cmu", path } }, Ct);

        await AssertProblem(HttpStatusCode.BadRequest, response);
        Assert.False(await ReadBack(db => db.ModuleScopes.AnyAsync(Ct)));
    }

    [Fact]
    public async Task Create_refuses_a_scope_with_no_paths()
    {
        await AssertProblem(HttpStatusCode.BadRequest, await RootClient.PostAsJsonAsync(Scopes, new { name = "scope", paths = Array.Empty<string>() }, Ct));
        Assert.False(await ReadBack(db => db.ModuleScopes.AnyAsync(Ct)));
    }

    [Fact]
    public async Task Create_refuses_a_name_in_use_ignoring_case_and_a_blank_name()
    {
        await Seed(TestData.ModuleScope("Taken", "cmu"));

        await AssertProblem(HttpStatusCode.Conflict, await RootClient.PostAsJsonAsync(Scopes, new { name = "taken", paths = new[] { "sei" } }, Ct));
        await AssertProblem(HttpStatusCode.BadRequest, await RootClient.PostAsJsonAsync(Scopes, new { name = " ", paths = new[] { "sei" } }, Ct));
    }

    [Fact]
    public async Task Edit_changes_name_description_and_paths()
    {
        var scope = TestData.ModuleScope("Old", "cmu");
        await Seed(scope);

        var response = await RootClient.PutAsJsonAsync($"{Scopes}/{scope.Id}", new { name = "New", description = "d", paths = new[] { "sei/team-a" } }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        var stored = await ReadBack(db => db.ModuleScopes.SingleAsync(x => x.Id == scope.Id, Ct));
        Assert.Equal("New", stored.Name);
        Assert.Equal(["sei/team-a"], stored.Paths);
    }

    [Fact]
    public async Task Edit_needs_ManageModules_and_an_existing_scope()
    {
        var scope = TestData.ModuleScope("Scope", "cmu");
        await Seed(scope);
        var viewer = await Actor().WithSystemPermissions(SystemPermission.ViewModules).SeedAsync();
        var body = new { name = "Scope", paths = new[] { "sei" } };

        await AssertProblem(HttpStatusCode.Forbidden, await Client(viewer).PutAsJsonAsync($"{Scopes}/{scope.Id}", body, Ct));
        await AssertProblem(HttpStatusCode.NotFound, await RootClient.PutAsJsonAsync($"{Scopes}/{Guid.NewGuid()}", body, Ct));
        Assert.Equal(["cmu"], (await ReadBack(db => db.ModuleScopes.SingleAsync(x => x.Id == scope.Id, Ct))).Paths);
    }

    [Fact]
    public async Task Delete_removes_the_scope_and_its_assignments_and_leaves_the_project_unrestricted()
    {
        var project = TestData.Project();
        var group = Group();
        var scope = TestData.ModuleScope("Scope", "cmu");
        await Seed(project, group, scope, TestData.ProjectModuleScope(project, scope), TestData.ProjectGroupModuleScope(group, scope));

        await AssertStatus(HttpStatusCode.NoContent, await RootClient.DeleteAsync($"{Scopes}/{scope.Id}", Ct));

        Assert.False(await ReadBack(db => db.ModuleScopes.AnyAsync(Ct)));
        Assert.False(await ReadBack(db => db.ProjectModuleScopes.AnyAsync(Ct)));
        Assert.False(await ReadBack(db => db.ProjectGroupModuleScopes.AnyAsync(Ct)));
        Assert.True(await ReadBack(db => db.Projects.AnyAsync(x => x.Id == project.Id, Ct)));
        Assert.False((await ReadAsync<AccessView>(await RootClient.GetAsync($"api/projects/{project.Id}/module-scopes", Ct))).Restricted);
    }

    [Fact]
    public async Task Delete_needs_ManageModules()
    {
        var scope = TestData.ModuleScope("Scope", "cmu");
        await Seed(scope);
        var viewer = await Actor().WithSystemPermissions(SystemPermission.ViewModules).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(viewer).DeleteAsync($"{Scopes}/{scope.Id}", Ct));
        Assert.True(await ReadBack(db => db.ModuleScopes.AnyAsync(Ct)));
    }

    // ---- read ------------------------------------------------------------------------------------

    [Fact]
    public async Task GetAll_and_Get_list_the_scopes_with_where_they_are_assigned_to_a_holder_of_ViewModules()
    {
        var project = TestData.Project("Alpha");
        var group = Group();
        var scope = TestData.ModuleScope("Scope", "sei", "cmu");
        await Seed(project, group, scope, TestData.ProjectModuleScope(project, scope), TestData.ProjectGroupModuleScope(group, scope));
        var viewer = await Actor().WithSystemPermissions(SystemPermission.ViewModules).SeedAsync();
        var nobody = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        var all = await ReadAsync<ScopeView[]>(await Client(viewer).GetAsync(Scopes, Ct));
        var one = await ReadAsync<ScopeView>(await Client(viewer).GetAsync($"{Scopes}/{scope.Id}", Ct));

        Assert.Equal(scope.Id, Assert.Single(all).Id);
        Assert.Equal(["cmu", "sei"], one.Paths);
        Assert.Equal(["Alpha"], one.Projects.Select(x => x.Name));
        Assert.Equal([group.Name], one.ProjectGroups.Select(x => x.Name));
        var manager = await Actor().WithSystemPermissions(SystemPermission.ManageModules).SeedAsync();
        await AssertStatus(HttpStatusCode.OK, await Client(manager).GetAsync(Scopes, Ct));
        await AssertStatus(HttpStatusCode.OK, await Client(manager).GetAsync($"{Scopes}/{scope.Id}", Ct));
        await AssertProblem(HttpStatusCode.Forbidden, await Client(nobody).GetAsync(Scopes, Ct));
        await AssertProblem(HttpStatusCode.Forbidden, await Client(nobody).GetAsync($"{Scopes}/{scope.Id}", Ct));
        await AssertProblem(HttpStatusCode.NotFound, await Client(viewer).GetAsync($"{Scopes}/{Guid.NewGuid()}", Ct));
    }

    [Fact]
    public async Task GetByProject_is_unrestricted_when_nothing_is_assigned()
    {
        var project = TestData.Project();
        await Seed(project, TestData.ModuleScope("Unassigned", "cmu"));
        var member = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var access = await ReadAsync<AccessView>(await Client(member).GetAsync($"api/projects/{project.Id}/module-scopes", Ct));

        Assert.False(access.Restricted);
        Assert.Empty(access.Paths);
        Assert.Empty(access.Scopes);
    }

    [Fact]
    public async Task GetByProject_gives_the_union_of_the_project_scopes_and_its_groups_with_each_origin()
    {
        var group = Group();
        var project = TestData.Project();
        project.ProjectGroupId = group.Id;
        var own = TestData.ModuleScope("Own", "cmu/exercise-x", "sei");
        var shared = TestData.ModuleScope("Shared", "sei", "cyber");
        var both = TestData.ModuleScope("Both", "uni");
        await Seed(group, project, own, shared, both,
            TestData.ProjectModuleScope(project, own),
            TestData.ProjectModuleScope(project, both),
            TestData.ProjectGroupModuleScope(group, shared),
            TestData.ProjectGroupModuleScope(group, both));
        var member = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var access = await ReadAsync<AccessView>(await Client(member).GetAsync($"api/projects/{project.Id}/module-scopes", Ct));

        Assert.True(access.Restricted);
        Assert.Equal(["cmu/exercise-x", "cyber", "sei", "uni"], access.Paths);
        Assert.Equal(["Both", "Own", "Shared"], access.Scopes.Select(x => x.Name));
        Assert.True(access.Scopes.Single(x => x.Name == "Own").IsDirect);
        Assert.False(access.Scopes.Single(x => x.Name == "Shared").IsDirect);
        var bothView = access.Scopes.Single(x => x.Name == "Both");
        Assert.True(bothView.IsDirect);
        Assert.Equal([group.Name], bothView.ProjectGroups.Select(x => x.Name));
    }

    [Fact]
    public async Task GetByProject_needs_ViewProject_on_that_project_or_ViewProjects()
    {
        var project = TestData.Project();
        await Seed(project);
        var other = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();
        var viewer = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(other).GetAsync($"api/projects/{project.Id}/module-scopes", Ct));
        await AssertStatus(HttpStatusCode.OK, await Client(viewer).GetAsync($"api/projects/{project.Id}/module-scopes", Ct));
        await AssertProblem(HttpStatusCode.BadRequest, await RootClient.GetAsync($"api/projects/{Guid.NewGuid()}/module-scopes", Ct));
    }

    // ---- assignments -----------------------------------------------------------------------------

    [Fact]
    public async Task Edit_assignments_adds_and_removes_a_scope_on_a_project_and_a_group_and_ignores_repeats()
    {
        var project = TestData.Project();
        var group = Group();
        var scope = TestData.ModuleScope("Scope", "cmu");
        var removed = TestData.ModuleScope("Removed", "sei");
        await Seed(project, group, scope, removed, TestData.ProjectModuleScope(project, removed));

        var response = await RootClient.PostAsJsonAsync(Assignments, new
        {
            changes = new object[]
            {
                new { operation = "Add", projectId = project.Id, moduleScopeId = scope.Id },
                new { operation = "Remove", projectId = project.Id, moduleScopeId = removed.Id },
                new { operation = "Add", projectGroupId = group.Id, moduleScopeId = scope.Id }
            }
        }, Ct);
        var again = await RootClient.PostAsJsonAsync(Assignments, new
        {
            changes = new object[]
            {
                new { operation = "Add", projectId = project.Id, moduleScopeId = scope.Id },
                new { operation = "Remove", projectId = project.Id, moduleScopeId = removed.Id }
            }
        }, Ct);

        await AssertStatus(HttpStatusCode.NoContent, response);
        await AssertStatus(HttpStatusCode.NoContent, again);
        Assert.Equal(scope.Id, Assert.Single(await ReadBack(db => db.ProjectModuleScopes.Where(x => x.ProjectId == project.Id).ToListAsync(Ct))).ModuleScopeId);
        Assert.Equal(scope.Id, Assert.Single(await ReadBack(db => db.ProjectGroupModuleScopes.Where(x => x.ProjectGroupId == group.Id).ToListAsync(Ct))).ModuleScopeId);
    }

    [Fact]
    public async Task Edit_assignments_of_a_scope_needs_ManageModules_not_ManageEnvironmentSets()
    {
        var project = TestData.Project();
        var scope = TestData.ModuleScope("Scope", "cmu");
        await Seed(project, scope);
        var sets = await Actor().WithSystemPermissions(SystemPermission.ManageEnvironmentSets, SystemPermission.ManageProjects).SeedAsync();
        var modules = await Actor().WithSystemPermissions(SystemPermission.ManageModules).SeedAsync();
        var member = await Actor().OnProject(project, [ProjectPermission.ViewProject, ProjectPermission.EditProject, ProjectPermission.ManageProject]).SeedAsync();
        var body = new { changes = new object[] { new { operation = "Add", projectId = project.Id, moduleScopeId = scope.Id } } };

        await AssertProblem(HttpStatusCode.Forbidden, await Client(sets).PostAsJsonAsync(Assignments, body, Ct));
        await AssertProblem(HttpStatusCode.Forbidden, await Client(member).PostAsJsonAsync(Assignments, body, Ct));
        Assert.False(await ReadBack(db => db.ProjectModuleScopes.AnyAsync(Ct)));
        await AssertStatus(HttpStatusCode.NoContent, await Client(modules).PostAsJsonAsync(Assignments, body, Ct));
    }

    [Fact]
    public async Task Edit_assignments_refuses_an_unknown_scope_and_changes_nothing()
    {
        var project = TestData.Project();
        var scope = TestData.ModuleScope("Scope", "cmu");
        await Seed(project, scope);

        var response = await RootClient.PostAsJsonAsync(Assignments, new
        {
            changes = new object[]
            {
                new { operation = "Add", projectId = project.Id, moduleScopeId = scope.Id },
                new { operation = "Add", projectId = project.Id, moduleScopeId = Guid.NewGuid() }
            }
        }, Ct);

        await AssertProblem(HttpStatusCode.NotFound, response);
        Assert.False(await ReadBack(db => db.ProjectModuleScopes.AnyAsync(Ct)));
    }

    [Fact]
    public async Task Edit_assignments_refuses_a_change_naming_a_scope_and_a_set_or_neither()
    {
        var project = TestData.Project();
        var scope = TestData.ModuleScope("Scope", "cmu");
        var set = TestData.EnvironmentSet();
        await Seed(project, scope, set);

        await AssertProblem(HttpStatusCode.BadRequest, await RootClient.PostAsJsonAsync(Assignments, new
        {
            changes = new object[] { new { operation = "Add", projectId = project.Id, moduleScopeId = scope.Id, environmentSetId = set.Id } }
        }, Ct));
        await AssertProblem(HttpStatusCode.BadRequest, await RootClient.PostAsJsonAsync(Assignments, new
        {
            changes = new object[] { new { operation = "Add", projectId = project.Id } }
        }, Ct));
    }
}
