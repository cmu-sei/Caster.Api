// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;

namespace Caster.Api.Tests.Features.ModuleScopes;

/// <summary>
/// With <c>Terraform:ModuleScopes:RestrictUnassigned</c> on, a project with no scope (its own or its group's) can
/// use only the shared source (<c>stock</c>) instead of every Module. Scoped projects are as before, plus it.
/// </summary>
public class RestrictUnassignedScopeTests(DatabaseFixture fixture, RestrictingSharedSourcesAppFactory factory)
    : SharedSourceScopeTestBase(fixture, factory), IClassFixture<RestrictingSharedSourcesAppFactory>
{
    [Fact]
    public async Task A_project_with_no_scope_sees_only_the_shared_source()
    {
        var catalog = await SeedCatalog();
        var project = await SeedProject();
        var member = await Member(project);

        Assert.Equal(Ids(catalog.Stock), await ListIds(member, project.Id));
        Assert.Equal(Ids(catalog.Stock), await ListIds(member));
    }

    [Fact]
    public async Task A_project_with_no_scope_in_a_group_with_no_scope_sees_only_the_shared_source()
    {
        var catalog = await SeedCatalog();
        var project = await SeedProject(await SeedGroup());
        var member = await Member(project);

        Assert.Equal(Ids(catalog.Stock), await ListIds(member, project.Id));
    }

    [Fact]
    public async Task A_scoped_project_sees_its_scope_and_the_shared_source()
    {
        var catalog = await SeedCatalog();
        var project = await SeedProject(null, "cmu");
        var member = await Member(project);

        Assert.Equal(Ids(catalog.Cmu, catalog.Stock), await ListIds(member, project.Id));
    }

    [Fact]
    public async Task A_group_scope_applies_to_its_projects_with_the_shared_source()
    {
        var catalog = await SeedCatalog();
        var project = await SeedProject(await SeedGroup("sei"));
        var member = await Member(project);

        Assert.Equal(Ids(catalog.Sei, catalog.Stock), await ListIds(member, project.Id));
    }

    [Fact]
    public async Task Without_a_project_a_member_gets_the_union_of_their_projects_and_never_every_module()
    {
        var catalog = await SeedCatalog();
        var unassigned = await SeedProject();
        var sei = await SeedProject(null, "sei");

        Assert.Equal(Ids(catalog.Sei, catalog.Stock), await ListIds(await Member(unassigned, sei)));
        Assert.Equal(Ids(catalog.Stock), await ListIds(await Member(unassigned)));
    }

    [Fact]
    public async Task A_holder_of_ViewModules_who_names_no_project_still_sees_every_module()
    {
        var catalog = await SeedCatalog();
        await SeedProject();
        var admin = await Actor().WithSystemPermissions(SystemPermission.ViewModules).SeedAsync();

        Assert.Equal(catalog.All, await ListIds(admin));
    }

    [Fact]
    public async Task A_holder_of_ViewModules_who_names_an_unassigned_project_is_held_to_its_access()
    {
        var catalog = await SeedCatalog();
        var project = await SeedProject();
        var admin = await Actor().WithSystemPermissions(SystemPermission.ViewModules, SystemPermission.ViewProjects).SeedAsync();

        Assert.Equal(Ids(catalog.Stock), await ListIds(admin, project.Id));
    }

    [Fact]
    public async Task Get_and_versions_give_an_unassigned_project_the_shared_source_only()
    {
        var catalog = await SeedCatalog();
        var project = await SeedProject();
        var member = await Member(project);

        await AssertGet(member, project, catalog.Stock, visible: true);
        await AssertGet(member, project, catalog.Cmu, visible: false);
        await AssertGet(member, project, catalog.Sei, visible: false);
    }

    [Fact]
    public async Task A_module_a_design_already_uses_keeps_working_for_an_unassigned_project()
    {
        var catalog = await SeedCatalog();
        var project = await SeedProject();
        var design = await SeedDesign(project);
        await Seed(TestData.DesignModule(design, catalog.Cmu));
        var member = await Member(project);

        await AssertGet(member, project, catalog.Cmu, visible: true);
        await AssertGet(member, project, catalog.Sei, visible: false);
    }

    [Fact]
    public async Task A_design_module_of_an_unassigned_project_may_only_come_from_the_shared_source()
    {
        var catalog = await SeedCatalog();
        var project = await SeedProject();
        var design = await SeedDesign(project);
        var editor = await Member(project);

        await AssertDesignModule(editor, design, catalog.Cmu, allowed: false);
        await AssertDesignModule(editor, design, catalog.Stock, allowed: true);
    }

    [Fact]
    public async Task The_snippet_of_an_unassigned_project_is_given_for_the_shared_source_only()
    {
        var catalog = await SeedCatalog();
        var project = await SeedProject();
        var member = await Member(project);

        await AssertSnippet(member, catalog.Stock, project, allowed: true);
        await AssertSnippet(member, catalog.Cmu, project, allowed: false);
    }

    [Fact]
    public async Task The_project_access_of_an_unassigned_project_says_it_is_restricted_to_the_shared_sources()
    {
        var project = await SeedProject();
        var member = await Member(project);

        var access = await GetAccess(member, project);

        Assert.True(access.Restricted);
        Assert.True(access.RestrictedBecauseUnassigned);
        Assert.Empty(access.Paths);
        Assert.Empty(access.PathOrigins);
        Assert.Equal(["stock"], access.SharedSources);
    }

    [Fact]
    public async Task The_project_access_of_a_scoped_project_is_not_restricted_because_of_the_setting()
    {
        var project = await SeedProject(null, "cmu");
        var member = await Member(project);

        var access = await GetAccess(member, project);

        Assert.True(access.Restricted);
        Assert.False(access.RestrictedBecauseUnassigned);
        Assert.Equal(["cmu"], access.Paths);
    }

    [Fact]
    public Task The_sources_report_which_are_shared() => AssertSourcesReportShared();
}
