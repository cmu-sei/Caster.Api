// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DesignModuleValidation = Caster.Api.Features.DesignModules.DesignModuleValidation;
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

    /// <summary>The version of <see cref="SeedModuleWithARequiredVariable"/>.</summary>
    private const string RequiredVersion = "1.0.0";

    /// <summary>The variable that version declares without a default.</summary>
    private const string RequiredVariable = "vm_name";

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
    public async Task Get_is_forbidden_for_a_caller_holding_only_EditProjects()
    {
        var (_, _, designModule) = await SeedDesignModule();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

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
    public async Task GetByDesign_lists_the_modules_for_a_caller_holding_ViewProjects()
    {
        var (_, design, designModule) = await SeedDesignModule();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var response = await Client(actor).GetAsync($"api/designs/{design.Id}/modules", Ct);

        Assert.Equal([designModule.Id], (await ReadAsync<DesignModuleView[]>(response)).Select(x => x.Id));
    }

    [Fact]
    public async Task GetByDesign_is_forbidden_for_a_caller_holding_only_EditProjects()
    {
        var (_, design, _) = await SeedDesignModule();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/designs/{design.Id}/modules", Ct));
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
    public async Task Create_is_forbidden_for_a_caller_holding_only_ViewProjects()
    {
        var (_, design) = await SeedDesign();
        var module = await SeedModule();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

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
    public async Task Create_answers_a_bad_request_for_a_version_the_module_does_not_have()
    {
        var (_, design) = await SeedDesign();
        var module = await SeedModule();

        var response = await RootClient.PostAsJsonAsync(
            "api/designModules", new { designId = design.Id, moduleId = module.Id, name = "web", moduleVersion = "9.9.9", values = NoValues }, Ct);

        var problem = await AssertProblem(HttpStatusCode.BadRequest, response);
        Assert.Equal(["Module version '9.9.9' does not exist"], Errors(problem, "ModuleVersion"));
        Assert.False(await AnyDesignModule(design.Id));
    }

    [Fact]
    public async Task Create_answers_a_bad_request_without_a_value_for_a_required_variable()
    {
        var (_, design) = await SeedDesign();
        var module = await SeedModuleWithARequiredVariable();

        var response = await RootClient.PostAsJsonAsync(
            "api/designModules", new { designId = design.Id, moduleId = module.Id, name = "web", moduleVersion = RequiredVersion, values = NoValues }, Ct);

        var problem = await AssertProblem(HttpStatusCode.BadRequest, response);
        Assert.Equal([$"Required values are missing: {RequiredVariable}"], Errors(problem, "Values"));
        Assert.False(await AnyDesignModule(design.Id));
    }

    /// <summary>A <c>${var.x}</c> reference counts as a value: it is set when Terraform plans.</summary>
    [Fact]
    public async Task Create_accepts_a_variable_reference_as_the_value_of_a_required_variable()
    {
        var (_, design) = await SeedDesign();
        var module = await SeedModuleWithARequiredVariable();

        var response = await RootClient.PostAsJsonAsync("api/designModules", new
        {
            designId = design.Id,
            moduleId = module.Id,
            name = "web",
            moduleVersion = RequiredVersion,
            values = new[] { new { name = RequiredVariable, value = "${var.name}" } }
        }, Ct);

        var created = await ReadAsync<DesignModuleView>(response);
        Assert.Equal("${var.name}", Assert.Single((await StoredDesignModule(created.Id)).Values).Value);
    }

    [Fact]
    public async Task Create_answers_a_bad_request_for_a_name_another_module_of_the_design_has()
    {
        var (_, design, designModule) = await SeedDesignModule();

        var response = await RootClient.PostAsJsonAsync(
            "api/designModules", new { designId = design.Id, moduleId = designModule.ModuleId, name = designModule.Name, moduleVersion = "1.0.0", values = NoValues }, Ct);

        var problem = await AssertProblem(HttpStatusCode.BadRequest, response);
        Assert.Equal([$"This design already has a module named '{designModule.Name}'"], Errors(problem, "Name"));
        Assert.Equal([designModule.Id], await DesignModuleIds(design.Id));
    }

    /// <summary>A module's name becomes a Terraform block label, which takes no space.</summary>
    [Fact]
    public async Task Create_answers_a_bad_request_for_a_name_terraform_rejects()
    {
        var (_, design) = await SeedDesign();
        var module = await SeedModule();

        var response = await RootClient.PostAsJsonAsync(
            "api/designModules", new { designId = design.Id, moduleId = module.Id, name = "Blank Machine", moduleVersion = "1.0.0", values = NoValues }, Ct);

        var problem = await AssertProblem(HttpStatusCode.BadRequest, response);
        Assert.Equal([DesignModuleValidation.TerraformNameMessage], Errors(problem, "Name"));
        Assert.False(await AnyDesignModule(design.Id));
    }

    [Fact]
    public async Task Create_answers_a_bad_request_without_a_version()
    {
        var (_, design) = await SeedDesign();
        var module = await SeedModule();

        var response = await RootClient.PostAsJsonAsync(
            "api/designModules", new { designId = design.Id, moduleId = module.Id, name = "web", values = NoValues }, Ct);

        var problem = await AssertProblem(HttpStatusCode.BadRequest, response);
        Assert.Equal(["'Module Version' must not be empty."], Errors(problem, "ModuleVersion"));
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
    public async Task Edit_answers_a_bad_request_for_a_version_the_module_does_not_have()
    {
        var (_, design, designModule) = await SeedDesignModule();

        var response = await RootClient.PutAsJsonAsync($"api/designModules/{designModule.Id}",
            new { designId = design.Id, moduleId = designModule.ModuleId, name = designModule.Name, moduleVersion = "9.9.9", values = NoValues }, Ct);

        var problem = await AssertProblem(HttpStatusCode.BadRequest, response);
        Assert.Equal(["Module version '9.9.9' does not exist"], Errors(problem, "ModuleVersion"));
        Assert.Equal("1.0.0", (await StoredDesignModule(designModule.Id)).ModuleVersion);
    }

    [Fact]
    public async Task Edit_answers_a_bad_request_for_a_name_terraform_rejects()
    {
        var (_, design, designModule) = await SeedDesignModule();

        var response = await RootClient.PutAsJsonAsync($"api/designModules/{designModule.Id}",
            new { designId = design.Id, moduleId = designModule.ModuleId, name = "Blank Machine", moduleVersion = "1.0.0", values = NoValues }, Ct);

        var problem = await AssertProblem(HttpStatusCode.BadRequest, response);
        Assert.Equal([DesignModuleValidation.TerraformNameMessage], Errors(problem, "Name"));
        Assert.Equal(designModule.Name, (await StoredDesignModule(designModule.Id)).Name);
    }

    [Fact]
    public async Task Edit_answers_a_bad_request_for_a_name_another_module_of_the_design_has()
    {
        var (_, design, designModule) = await SeedDesignModule();
        var other = TestData.DesignModule(design, await SeedModule(), "other");
        await Seed(other);

        var response = await RootClient.PutAsJsonAsync($"api/designModules/{designModule.Id}",
            new { designId = design.Id, moduleId = designModule.ModuleId, name = "other", moduleVersion = "1.0.0", values = NoValues }, Ct);

        var problem = await AssertProblem(HttpStatusCode.BadRequest, response);
        Assert.Equal(["This design already has a module named 'other'"], Errors(problem, "Name"));
        Assert.Equal(designModule.Name, (await StoredDesignModule(designModule.Id)).Name);
    }

    /// <summary>An edit that sends no values keeps the stored ones, and the required variable stays set.</summary>
    [Fact]
    public async Task Edit_without_values_keeps_the_stored_value_of_a_required_variable()
    {
        var designModule = await SeedDesignModuleWithItsRequiredValue();

        var response = await RootClient.PutAsJsonAsync($"api/designModules/{designModule.Id}",
            new { designId = designModule.DesignId, moduleId = designModule.ModuleId, name = "renamed", moduleVersion = RequiredVersion, values = NoValues }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        var stored = await StoredDesignModule(designModule.Id);
        Assert.Equal("renamed", stored.Name);
        Assert.Equal("web", Assert.Single(stored.Values, x => x.Name == RequiredVariable).Value);
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
    public async Task Edit_is_forbidden_for_a_caller_holding_only_ViewProjects()
    {
        var (_, design, designModule) = await SeedDesignModule();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

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

    /// <summary>An edit naming a design of another project stores the change and leaves the module in its own design.</summary>
    [Fact]
    public async Task Edit_keeps_the_design_module_in_its_design_when_the_body_names_a_design_of_another_project()
    {
        var (project, design, designModule) = await SeedDesignModule();
        var (_, foreignDesign) = await SeedDesign();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/designModules/{designModule.Id}",
            new { designId = foreignDesign.Id, moduleId = designModule.ModuleId, name = designModule.Name, moduleVersion = "2.0.0", values = NoValues }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        var stored = await StoredDesignModule(designModule.Id);
        Assert.Equal(design.Id, stored.DesignId);
        Assert.Equal("2.0.0", stored.ModuleVersion);
    }

    // ---- PUT api/designModules/{id}: the stored design module decides ---------------------------

    /// <summary>
    /// A <c>designId</c> in the body, of another project's design, of nothing, null or left out, is ignored:
    /// the module stays in its design, and its name, version and values change.
    /// </summary>
    [Theory]
    [InlineData("foreign")]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("omitted")]
    public async Task Edit_ignores_legacy_design_fields_and_still_updates_values(string parent)
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.EditProject]).SeedAsync();
        var body = new Dictionary<string, object>
        {
            ["name"] = "changed",
            ["moduleId"] = graph.Module.Id,
            ["moduleVersion"] = "v2",
            ["values"] = new[] { new { name = "input", value = "changed" } }
        };
        WithLegacyField(body, "designId", parent, graph.DesignB.Id);

        var response = await Client(actor).PutAsJsonAsync($"api/designModules/{graph.DesignModuleA.Id}", body, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        var stored = await StoredDesignModule(graph.DesignModuleA.Id);
        Assert.Equal(graph.DesignA.Id, stored.DesignId);
        Assert.Equal("changed", stored.Name);
        Assert.Equal("v2", stored.ModuleVersion);
        Assert.Equal("changed", Assert.Single(stored.Values).Value);
    }

    /// <summary>The design module on the route is authorized before any value changes, whatever the body names.</summary>
    [Fact]
    public async Task Edit_authorizes_the_route_before_changing_values()
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/designModules/{graph.DesignModuleB.Id}", new
        {
            designModuleId = graph.DesignModuleA.Id,
            designId = graph.DesignA.Id,
            moduleId = graph.Module.Id,
            name = "changed",
            moduleVersion = "v2",
            values = new[] { new { name = "input", value = "changed" } }
        }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        var stored = await StoredDesignModule(graph.DesignModuleB.Id);
        Assert.Equal(graph.DesignB.Id, stored.DesignId);
        Assert.Equal("victim", stored.Name);
        Assert.Equal("v1", stored.ModuleVersion);
        Assert.Equal("victim", Assert.Single(stored.Values).Value);
    }

    /// <summary>A create still takes the design its body names.</summary>
    [Fact]
    public async Task Create_adds_the_module_to_the_design_its_body_names()
    {
        var graph = await SeedGraph();
        var actor = await Actor().OnProject(graph.ProjectA, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/designModules",
            new { designId = graph.DesignA.Id, moduleId = graph.Module.Id, name = "created", moduleVersion = "v1", values = NoValues }, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        var created = await ReadAsync<DesignModuleView>(response);
        Assert.Equal(graph.DesignA.Id, (await StoredDesignModule(created.Id)).DesignId);
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
    public async Task Disable_is_forbidden_for_a_caller_holding_only_ViewProjects()
    {
        var (_, _, designModule) = await SeedDesignModule();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

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
    public async Task AddOrUpdateValues_by_a_caller_holding_EditProjects_stores_the_values()
    {
        var (_, _, designModule) = await SeedDesignModule();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(
            $"api/designModules/{designModule.Id}/values", new { values = new[] { new { name = "count", value = "3" } } }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        var stored = await StoredDesignModule(designModule.Id);
        Assert.Equal("3", Assert.Single(stored.Values, x => x.Name == "count").Value);
    }

    [Fact]
    public async Task AddOrUpdateValues_is_forbidden_for_a_caller_holding_only_ViewProjects()
    {
        var (_, _, designModule) = await SeedDesignModule();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(
            $"api/designModules/{designModule.Id}/values", new { values = new[] { new { name = "count", value = "3" } } }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
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

    [Fact]
    public async Task AddOrUpdateValues_answers_a_bad_request_for_a_blank_value_of_a_required_variable()
    {
        var designModule = await SeedDesignModuleWithItsRequiredValue();

        var response = await RootClient.PostAsJsonAsync(
            $"api/designModules/{designModule.Id}/values", new { values = new[] { new { name = RequiredVariable, value = "" } } }, Ct);

        var problem = await AssertProblem(HttpStatusCode.BadRequest, response);
        Assert.Equal([$"Required values are missing: {RequiredVariable}"], Errors(problem, "Values"));
        Assert.Equal("web", Assert.Single((await StoredDesignModule(designModule.Id)).Values, x => x.Name == RequiredVariable).Value);
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

        var response = await Client(actor).DeleteAsync($"api/designModules/{designModule.Id}", Ct);

        await AssertStatus(HttpStatusCode.NoContent, response);
        Assert.Null(await StoredDesignModule(designModule.Id));
    }

    [Fact]
    public async Task Delete_by_a_caller_holding_EditProjects_removes_the_design_module()
    {
        var (_, _, designModule) = await SeedDesignModule();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.NoContent, await Client(actor).DeleteAsync($"api/designModules/{designModule.Id}", Ct));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, _, designModule) = await SeedDesignModule();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/designModules/{designModule.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.NotNull(await StoredDesignModule(designModule.Id));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_holding_only_ViewProjects()
    {
        var (_, _, designModule) = await SeedDesignModule();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/designModules/{designModule.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.NotNull(await StoredDesignModule(designModule.Id));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, _, designModule) = await SeedDesignModule();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/designModules/{designModule.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.NotNull(await StoredDesignModule(designModule.Id));
    }

    /// <summary>The misspelled api/designsModules route stays as an alias of api/designModules/{id}.</summary>
    [Fact]
    public async Task Delete_through_the_obsolete_route_by_a_member_holding_EditProject_removes_the_design_module()
    {
        var (project, _, designModule) = await SeedDesignModule();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/designsModules/{designModule.Id}", Ct);

        await AssertStatus(HttpStatusCode.NoContent, response);
        Assert.Null(await StoredDesignModule(designModule.Id));
    }

    [Fact]
    public async Task Delete_through_the_obsolete_route_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, _, designModule) = await SeedDesignModule();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/designsModules/{designModule.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.NotNull(await StoredDesignModule(designModule.Id));
    }

    private Task<TwoProjectGraph> SeedGraph() => TwoProjectGraph.SeedAsync(Db, Root.Id, Ct);

    /// <summary>
    /// Sets <paramref name="field"/> of <paramref name="body"/> as an old client would send it: the id of a row
    /// of another project (<c>foreign</c>), an id nothing has (<c>missing</c>), <c>null</c>, or left out
    /// (<c>omitted</c>).
    /// </summary>
    private static void WithLegacyField(Dictionary<string, object> body, string field, string scenario, Guid foreignId)
    {
        if (scenario != "omitted")
        {
            body[field] = scenario switch { "foreign" => foreignId, "missing" => Guid.NewGuid(), _ => null };
        }
    }

    private async Task<(Project Project, Design Design)> SeedDesign()
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        var design = TestData.Design(directory);
        await Seed(project, directory, design);

        return (project, design);
    }

    /// <summary>
    /// A module with the two versions the bodies above name, neither with a required variable, so that Create,
    /// Edit and AddOrUpdateValues find the version they check for and nothing missing.
    /// </summary>
    private async Task<Module> SeedModule()
    {
        var module = TestData.Module();
        await Seed(module, TestData.ModuleVersion(module, "1.0.0"), TestData.ModuleVersion(module, "2.0.0"));

        return module;
    }

    /// <summary>A module whose one version, <see cref="RequiredVersion"/>, requires <see cref="RequiredVariable"/>.</summary>
    private async Task<Module> SeedModuleWithARequiredVariable()
    {
        var module = TestData.Module();
        var version = TestData.ModuleVersion(module, RequiredVersion);
        version.Variables = [new ModuleVariable { Name = RequiredVariable, VariableType = "string" }];
        await Seed(module, version);

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

    /// <summary>A design module of <see cref="SeedModuleWithARequiredVariable"/>'s module, its required variable set to "web".</summary>
    private async Task<DesignModule> SeedDesignModuleWithItsRequiredValue()
    {
        var (_, design) = await SeedDesign();
        var module = await SeedModuleWithARequiredVariable();
        var designModule = TestData.DesignModule(design, module);
        designModule.ModuleVersion = RequiredVersion;
        designModule.Values = [new ModuleValue { Name = RequiredVariable, Value = "web" }];
        await Seed(designModule);

        return designModule;
    }

    /// <summary>The messages a 400 lists for <paramref name="field"/>.</summary>
    private static string[] Errors(ProblemDetails problem, string field) =>
        [.. ((JsonElement)problem.Extensions["errors"]).GetProperty(field).EnumerateArray().Select(x => x.GetString())];

    private Task<Guid[]> DesignModuleIds(Guid designId) =>
        ReadBack(db => db.DesignModules.Where(x => x.DesignId == designId).Select(x => x.Id).ToArrayAsync(Ct));

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
