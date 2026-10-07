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
using VariableView = Caster.Api.Features.Variables.Variable;

namespace Caster.Api.Tests.Features.Variables;

/// <summary>
/// <c>VariablesController</c>: a design's variables are read with ViewProject (or ViewProjects) and changed
/// with EditProject (or EditProjects) on the design's project.
/// </summary>
public class VariableRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    [Fact]
    public async Task Get_returns_the_variable_to_a_member_holding_ViewProject()
    {
        var (project, _, variable) = await SeedVariable();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/variables/{variable.Id}", Ct);

        Assert.Equal(variable.Name, (await ReadAsync<VariableView>(response)).Name);
    }

    [Fact]
    public async Task Get_returns_the_variable_to_a_caller_holding_ViewProjects()
    {
        var (_, _, variable) = await SeedVariable();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).GetAsync($"api/variables/{variable.Id}", Ct));
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var (project, _, variable) = await SeedVariable();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/variables/{variable.Id}", Ct));
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_ViewProject_only_on_another_project()
    {
        var (_, _, variable) = await SeedVariable();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/variables/{variable.Id}", Ct));
    }

    [Fact]
    public async Task GetByDesign_lists_the_variables_for_a_member_holding_ViewProject()
    {
        var (project, design, variable) = await SeedVariable();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/designss/{design.Id}/variables", Ct);

        Assert.Equal([variable.Id], (await ReadAsync<VariableView[]>(response)).Select(x => x.Id));
    }

    [Fact]
    public async Task GetByDesign_is_forbidden_for_a_caller_holding_ViewProject_only_on_another_project()
    {
        var (_, design, _) = await SeedVariable();
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/designss/{design.Id}/variables", Ct));
    }

    [Fact]
    public async Task GetByDesign_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var (project, design, _) = await SeedVariable();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/designss/{design.Id}/variables", Ct));
    }

    [Fact]
    public async Task Create_by_a_member_holding_EditProject_stores_the_variable()
    {
        var (project, design) = await SeedDesign();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(
            "api/variables", new { designId = design.Id, name = "count", type = "number", defaultValue = "2" }, Ct);

        var created = await ReadAsync<VariableView>(response);
        var stored = await StoredVariable(created.Id);
        Assert.Equal(VariableType.number, stored.Type);
        Assert.Equal("2", stored.DefaultValue);
    }

    [Fact]
    public async Task Create_by_a_caller_holding_EditProjects_stores_the_variable()
    {
        var (_, design) = await SeedDesign();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/variables", new { designId = design.Id, name = "count", type = "number" }, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, design) = await SeedDesign();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/variables", new { designId = design.Id, name = "refused", type = "string" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.False(await VariableNamed("refused"));
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, design) = await SeedDesign();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/variables", new { designId = design.Id, name = "refused", type = "string" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.False(await VariableNamed("refused"));
    }

    [Fact]
    public async Task Edit_by_a_member_holding_EditProject_stores_the_new_default()
    {
        var (project, _, variable) = await SeedVariable();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/variables/{variable.Id}", new { name = variable.Name, type = "string", defaultValue = "changed" }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("changed", (await StoredVariable(variable.Id)).DefaultValue);
    }

    [Fact]
    public async Task Edit_by_a_caller_holding_EditProjects_stores_the_change()
    {
        var (_, _, variable) = await SeedVariable();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/variables/{variable.Id}", new { name = variable.Name, type = "string", defaultValue = "changed" }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
    }

    [Fact]
    public async Task Edit_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, _, variable) = await SeedVariable();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/variables/{variable.Id}", new { name = variable.Name, type = "string", defaultValue = "changed" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(variable.DefaultValue, (await StoredVariable(variable.Id)).DefaultValue);
    }

    [Fact]
    public async Task Edit_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, _, variable) = await SeedVariable();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/variables/{variable.Id}", new { name = variable.Name, type = "string", defaultValue = "changed" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal(variable.DefaultValue, (await StoredVariable(variable.Id)).DefaultValue);
    }

    [Fact]
    public async Task Delete_by_a_member_holding_EditProject_removes_the_variable()
    {
        var (project, _, variable) = await SeedVariable();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/variables/{variable.Id}", Ct);

        await AssertStatus(HttpStatusCode.NoContent, response);
        Assert.Null(await StoredVariable(variable.Id));
    }

    [Fact]
    public async Task Delete_by_a_caller_holding_EditProjects_removes_the_variable()
    {
        var (_, _, variable) = await SeedVariable();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.NoContent, await Client(actor).DeleteAsync($"api/variables/{variable.Id}", Ct));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, _, variable) = await SeedVariable();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/variables/{variable.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.NotNull(await StoredVariable(variable.Id));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_holding_EditProject_only_on_another_project()
    {
        var (_, _, variable) = await SeedVariable();
        var actor = await Actor().OnNewProject(ProjectPermission.EditProject).SeedAsync();

        var response = await Client(actor).DeleteAsync($"api/variables/{variable.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.NotNull(await StoredVariable(variable.Id));
    }

    private async Task<(Project Project, Design Design)> SeedDesign()
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        var design = TestData.Design(directory);
        await Seed(project, directory, design);

        return (project, design);
    }

    private async Task<(Project Project, Design Design, Variable Variable)> SeedVariable()
    {
        var (project, design) = await SeedDesign();
        var variable = TestData.Variable(design);
        await Seed(variable);

        return (project, design, variable);
    }

    private async Task<Variable> StoredVariable(Guid id)
    {
        await using var context = NewContext();

        return await context.Variables.SingleOrDefaultAsync(x => x.Id == id, Ct);
    }

    private async Task<bool> VariableNamed(string name)
    {
        await using var context = NewContext();

        return await context.Variables.AnyAsync(x => x.Name == name, Ct);
    }
}
