// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;

namespace Caster.Api.Tests.Features.ModuleScopes;

/// <summary>
/// A shared source (<c>stock</c>) is available to every project, scoped or not, and cannot be restricted, with
/// <c>Terraform:ModuleScopes:RestrictUnassigned</c> off: a project with no scope still sees every Module.
/// </summary>
public class SharedSourceScopeTests(DatabaseFixture fixture, SharedSourcesAppFactory factory)
    : SharedSourceScopeTestBase(fixture, factory), IClassFixture<SharedSourcesAppFactory>
{
    [Fact]
    public async Task A_project_whose_scope_does_not_mention_the_shared_source_still_sees_it()
    {
        var catalog = await SeedCatalog();
        var project = await SeedProject(null, "cmu");
        var member = await Member(project);

        Assert.Equal(Ids(catalog.Cmu, catalog.Stock), await ListIds(member, project.Id));
        Assert.Equal(Ids(catalog.Cmu, catalog.Stock), await ListIds(member));
    }

    [Fact]
    public async Task The_shared_source_and_the_scopes_of_the_project_and_its_group_are_united()
    {
        var catalog = await SeedCatalog();
        var group = await SeedGroup("cmu");
        var project = await SeedProject(group, "sei");
        var member = await Member(project);

        Assert.Equal(catalog.All, await ListIds(member, project.Id));
    }

    [Fact]
    public async Task A_group_scope_gives_its_projects_the_shared_source_with_it()
    {
        var catalog = await SeedCatalog();
        var group = await SeedGroup("sei");
        var project = await SeedProject(group);
        var member = await Member(project);

        Assert.Equal(Ids(catalog.Sei, catalog.Stock), await ListIds(member, project.Id));
    }

    [Fact]
    public async Task A_project_with_no_scope_in_a_group_with_no_scope_sees_every_module()
    {
        var catalog = await SeedCatalog();
        var project = await SeedProject(await SeedGroup());
        var member = await Member(project);

        Assert.Equal(catalog.All, await ListIds(member, project.Id));
    }

    [Fact]
    public async Task A_project_with_no_scope_sees_every_module()
    {
        var catalog = await SeedCatalog();
        var project = await SeedProject();
        var member = await Member(project);

        Assert.Equal(catalog.All, await ListIds(member, project.Id));
    }

    [Fact]
    public async Task Without_a_project_a_member_gets_the_union_of_their_projects_and_the_shared_source()
    {
        var catalog = await SeedCatalog();
        var cmu = await SeedProject(null, "cmu");
        var sei = await SeedProject(null, "sei");

        Assert.Equal(Ids(catalog.Cmu, catalog.Stock), await ListIds(await Member(cmu)));
        Assert.Equal(catalog.All, await ListIds(await Member(cmu, sei)));
    }

    [Fact]
    public async Task Without_a_project_a_member_of_an_unscoped_project_gets_every_module()
    {
        var catalog = await SeedCatalog();
        var member = await Member(await SeedProject(null, "cmu"), await SeedProject());

        Assert.Equal(catalog.All, await ListIds(member));
    }

    [Fact]
    public async Task A_holder_of_ViewModules_who_names_no_project_still_sees_every_module()
    {
        var catalog = await SeedCatalog();
        await SeedProject(null, "cmu");
        var admin = await Actor().WithSystemPermissions(SystemPermission.ViewModules).SeedAsync();

        Assert.Equal(catalog.All, await ListIds(admin));
    }

    [Fact]
    public async Task Get_and_versions_give_the_shared_source_to_a_scoped_project_and_hide_another_vendors()
    {
        var catalog = await SeedCatalog();
        var project = await SeedProject(null, "cmu");
        var member = await Member(project);

        await AssertGet(member, project, catalog.Stock, visible: true);
        await AssertGet(member, project, catalog.Cmu, visible: true);
        await AssertGet(member, project, catalog.Sei, visible: false);
    }

    [Fact]
    public async Task A_design_module_may_come_from_the_shared_source_or_the_projects_scope_and_not_another_vendor()
    {
        var catalog = await SeedCatalog();
        var project = await SeedProject(null, "cmu");
        var design = await SeedDesign(project);
        var editor = await Member(project);

        await AssertDesignModule(editor, design, catalog.Sei, allowed: false);
        await AssertDesignModule(editor, design, catalog.Stock, allowed: true);
    }

    [Fact]
    public async Task The_snippet_is_given_for_the_shared_source_and_not_for_another_vendor()
    {
        var catalog = await SeedCatalog();
        var project = await SeedProject(null, "cmu");
        var member = await Member(project);

        await AssertSnippet(member, catalog.Stock, project, allowed: true);
        await AssertSnippet(member, catalog.Cmu, project, allowed: true);
        await AssertSnippet(member, catalog.Sei, project, allowed: false);
    }

    [Fact]
    public async Task A_module_a_design_already_uses_keeps_working_outside_the_scopes()
    {
        var catalog = await SeedCatalog();
        var project = await SeedProject(null, "cmu");
        var design = await SeedDesign(project);
        await Seed(TestData.DesignModule(design, catalog.Sei));
        var member = await Member(project);

        await AssertGet(member, project, catalog.Sei, visible: true);
    }

    [Fact]
    public async Task The_project_access_reports_the_union_the_shared_sources_and_where_each_path_comes_from()
    {
        await SeedCatalog();
        var group = await SeedGroup("sei", "cmu");
        var project = await SeedProject(group, "cmu");
        var member = await Member(project);

        var access = await GetAccess(member, project);

        Assert.True(access.Restricted);
        Assert.False(access.RestrictedBecauseUnassigned);
        Assert.Equal(["cmu", "sei"], access.Paths);
        Assert.Equal(["stock"], access.SharedSources);
        var cmu = Assert.Single(access.PathOrigins, x => x.Path == "cmu");
        Assert.Equal(2, cmu.Scopes.Length);
        Assert.Contains(cmu.Scopes, x => x.IsDirect);
        Assert.Contains(cmu.Scopes, x => x.ProjectGroups.Any(g => g.ProjectGroupId == group.Id));
        var sei = Assert.Single(access.PathOrigins, x => x.Path == "sei");
        Assert.Equal("Group scope", Assert.Single(sei.Scopes).Name);
    }

    [Fact]
    public async Task The_project_access_of_an_unscoped_project_is_unrestricted_and_still_names_the_shared_sources()
    {
        var project = await SeedProject();
        var member = await Member(project);

        var access = await GetAccess(member, project);

        Assert.False(access.Restricted);
        Assert.False(access.RestrictedBecauseUnassigned);
        Assert.Empty(access.Paths);
        Assert.Equal(["stock"], access.SharedSources);
    }

    [Fact]
    public Task The_sources_report_which_are_shared() => AssertSourcesReportShared();
}
