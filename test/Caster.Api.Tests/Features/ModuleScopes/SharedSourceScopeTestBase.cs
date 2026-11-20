// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using ModuleView = Caster.Api.Features.Modules.Module;

namespace Caster.Api.Tests.Features.ModuleScopes;

/// <summary>
/// What the two source-sharing hosts have in common: sources <c>cmu</c> and <c>sei</c> (one vendor repository
/// each) and <c>stock</c>, which is shared, with one Module in each. See <see cref="SharedSourcesAppFactory"/>.
/// </summary>
public abstract class SharedSourceScopeTestBase(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    protected static readonly object[] NoValues = [];

    protected record Catalog(Module Cmu, Module Sei, Module Stock)
    {
        public Guid[] All => Ids(Cmu, Sei, Stock);
    }

    protected record AccessView(
        bool Restricted, bool RestrictedBecauseUnassigned, string[] Paths, string[] SharedSources, PathOrigin[] PathOrigins, AccessScope[] Scopes);

    protected record PathOrigin(string Path, AccessScope[] Scopes);

    protected record AccessScope(string Name, bool IsDirect, Origin[] ProjectGroups);

    protected record Origin(Guid ProjectGroupId, string Name);

    protected record SourceView(string Name, bool Shared);

    protected static Guid[] Ids(params Module[] modules) => modules.Select(x => x.Id).Order().ToArray();

    protected async Task<Catalog> SeedCatalog()
    {
        Module Make(string path)
        {
            var module = TestData.Module(path.Replace('/', '-'));
            module.Path = path;
            return module;
        }

        var catalog = new Catalog(Make("cmu/a"), Make("sei/b"), Make("stock/c"));
        Module[] all = [catalog.Cmu, catalog.Sei, catalog.Stock];
        await Seed(all.Cast<object>().Concat(all.Select(x => (object)TestData.ModuleVersion(x))).ToArray());

        return catalog;
    }

    /// <summary>A project (in <paramref name="group"/>, if given) with the scopes of the given paths assigned to it.</summary>
    protected async Task<Project> SeedProject(ProjectGroup group = null, params string[] ownPaths)
    {
        var project = TestData.Project();
        project.ProjectGroupId = group?.Id;
        await Seed(project);

        if (ownPaths.Length > 0)
        {
            var scope = TestData.ModuleScope(null, ownPaths);
            await Seed(scope, TestData.ProjectModuleScope(project, scope));
        }

        return project;
    }

    protected async Task<ProjectGroup> SeedGroup(params string[] paths)
    {
        var group = new ProjectGroup { Id = Guid.NewGuid(), Name = $"group-{Guid.NewGuid():N}" };
        await Seed(group);

        if (paths.Length > 0)
        {
            var scope = TestData.ModuleScope("Group scope", paths);
            await Seed(scope, TestData.ProjectGroupModuleScope(group, scope));
        }

        return group;
    }

    protected Task<TestActor> Member(params Project[] projects)
    {
        var builder = Actor();
        foreach (var project in projects)
            builder = builder.OnProject(project, [ProjectPermission.ViewProject, ProjectPermission.EditProject]);

        return builder.SeedAsync();
    }

    protected async Task<Guid[]> ListIds(TestActor actor, Guid? projectId = null)
    {
        var response = await Client(actor).GetAsync(projectId is null ? "api/modules" : $"api/modules?projectId={projectId}", Ct);
        return (await ReadAsync<ModuleView[]>(response)).Select(x => x.Id).Order().ToArray();
    }

    /// <summary>Whether GET api/modules/{id} for the project gives the Module (200) or the scopes hide it (404).</summary>
    protected async Task AssertGet(TestActor actor, Project project, Module module, bool visible)
    {
        var get = await Client(actor).GetAsync($"api/modules/{module.Id}?projectId={project.Id}", Ct);
        var versions = await Client(actor).GetAsync($"api/modules/{module.Id}/versions?projectId={project.Id}", Ct);

        if (visible)
        {
            await AssertStatus(HttpStatusCode.OK, get);
            // Listing versions answers 500 whatever the scopes (the known defect ModuleRequestTests pins),
            // so a request that passed the gate is one that is neither 403 nor 404.
            Assert.NotEqual(HttpStatusCode.NotFound, versions.StatusCode);
            Assert.NotEqual(HttpStatusCode.Forbidden, versions.StatusCode);
        }
        else
        {
            await AssertProblem(HttpStatusCode.NotFound, get);
            await AssertProblem(HttpStatusCode.NotFound, versions);
        }
    }

    protected async Task<HttpResponseMessage> AddToDesign(TestActor actor, Design design, Module module) =>
        await Client(actor).PostAsJsonAsync(
            "api/designModules", new { designId = design.Id, moduleId = module.Id, name = "web", moduleVersion = "1.0.0", values = NoValues }, Ct);

    protected async Task<Design> SeedDesign(Project project)
    {
        var directory = TestData.Directory(project);
        var design = TestData.Design(directory);
        await Seed(directory, design);

        return design;
    }

    protected async Task<HttpResponseMessage> Snippet(TestActor actor, Module module, Project project)
    {
        var version = await ReadBack(db => db.ModuleVersions.FirstAsync(x => x.ModuleId == module.Id, Ct));
        return await Client(actor).PostAsJsonAsync(
            "api/modules/snippet", new { versionId = version.Id, moduleName = "web", variableValues = Array.Empty<object>(), projectId = project.Id }, Ct);
    }

    protected async Task<AccessView> GetAccess(TestActor actor, Project project) =>
        await ReadAsync<AccessView>(await Client(actor).GetAsync($"api/projects/{project.Id}/module-scopes", Ct));

    protected async Task AssertSnippet(TestActor actor, Module module, Project project, bool allowed)
    {
        var response = await Snippet(actor, module, project);

        if (allowed)
            await AssertStatus(HttpStatusCode.OK, response);
        else
            await AssertProblem(HttpStatusCode.NotFound, response);
    }

    protected async Task AssertDesignModule(TestActor actor, Design design, Module module, bool allowed)
    {
        var response = await AddToDesign(actor, design, module);

        if (allowed)
            await AssertStatus(HttpStatusCode.Created, response);
        else
            await AssertProblem(HttpStatusCode.BadRequest, response);
    }

    protected async Task AssertSourcesReportShared()
    {
        var admin = await Actor().WithSystemPermissions(SystemPermission.ViewModules).SeedAsync();

        var sources = await ReadAsync<SourceView[]>(await Client(admin).GetAsync("api/modules/sources", Ct));

        Assert.Equal(
            [("cmu", false), ("sei", false), ("stock", true)],
            sources.OrderBy(x => x.Name, StringComparer.Ordinal).Select(x => (x.Name, x.Shared)).ToArray());
    }
}
