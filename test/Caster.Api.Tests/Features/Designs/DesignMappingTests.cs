// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Hubs;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using DesignModuleView = Caster.Api.Features.DesignModules.DesignModule;
using DesignView = Caster.Api.Features.Designs.Design;
using VariableView = Caster.Api.Features.Variables.Variable;

namespace Caster.Api.Tests.Features.Designs;

/// <summary>
/// What designs, their modules and their variables are answered with: a design's modules and a design
/// module's values are left out of some reads and carried by others, and the variable's Terraform reference.
/// </summary>
public class DesignMappingTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    // ---- Designs ----------------------------------------------------------------------------------

    /// <summary>A single design read leaves its modules null; the directory's design list maps them, as an empty list when none are loaded.</summary>
    [Fact]
    public async Task A_design_read_leaves_the_modules_null_and_the_directory_list_answers_an_empty_list()
    {
        var graph = await SeedGraph();

        var one = await ReadAsync<JsonElement>(await RootClient.GetAsync($"api/designs/{graph.Design.Id}", Ct));
        var list = await ReadAsync<JsonElement[]>(await RootClient.GetAsync($"api/directories/{graph.Design.DirectoryId}/designs", Ct));

        AssertDesign(one, graph.Design);
        Assert.Equal(JsonValueKind.Null, one.GetProperty("modules").ValueKind);
        var listed = Assert.Single(list);
        AssertDesign(listed, graph.Design);
        Assert.Equal(0, listed.GetProperty("modules").GetArrayLength());
    }

    /// <summary>The design writes answer an empty module list, and so does their broadcast.</summary>
    [Fact]
    public async Task Design_writes_and_their_broadcasts_answer_an_empty_module_list()
    {
        var graph = await SeedGraph();

        var created = await ReadAsync<JsonElement>(await RootClient.PostAsJsonAsync("api/designs", new { name = "created", directoryId = graph.Design.DirectoryId }, Ct));
        var put = await ReadAsync<JsonElement>(await RootClient.PutAsJsonAsync($"api/designs/{graph.Design.Id}", new { name = "renamed" }, Ct));
        var enabled = await ReadAsync<JsonElement>(await RootClient.PostAsync($"api/designs/{graph.Design.Id}/actions/enable", null, Ct));

        Assert.Equal(("created", true), (created.GetProperty("name").GetString(), created.GetProperty("enabled").GetBoolean()));
        Assert.Equal("renamed", put.GetProperty("name").GetString());
        Assert.True(enabled.GetProperty("enabled").GetBoolean());
        Assert.All(new[] { created, put, enabled }, x => Assert.Equal(0, x.GetProperty("modules").GetArrayLength()));
        Assert.Equal("renamed", (await ReadBack(db => db.Designs.SingleAsync(x => x.Id == graph.Design.Id, Ct))).Name);
        var broadcast = Assert.IsType<DesignView>(Assert.Single(Factory.Hub<ProjectHub>().ToGroup(graph.Project.Id), x => x.Method == "DesignCreated").Arguments[0]);
        Assert.NotNull(broadcast.Modules);
        Assert.Empty(broadcast.Modules);
    }

    // ---- Design modules ---------------------------------------------------------------------------

    /// <summary>
    /// A single design module read carries its values, and so does the design's module list, so each
    /// module there can say which of its values are still placeholders.
    /// </summary>
    [Fact]
    public async Task A_design_module_read_and_the_list_carry_the_values()
    {
        var graph = await SeedGraph();

        var one = await ReadAsync<JsonElement>(await RootClient.GetAsync($"api/designModules/{graph.DesignModule.Id}", Ct));
        var list = await ReadAsync<JsonElement[]>(await RootClient.GetAsync($"api/designs/{graph.Design.Id}/modules", Ct));

        AssertDesignModule(one, graph);
        AssertValues(one, ("count", "3"));
        var listed = Assert.Single(list);
        AssertDesignModule(listed, graph);
        AssertValues(listed, ("count", "3"));
    }

    /// <summary>The design module writes answer the values, and so does their broadcast.</summary>
    [Fact]
    public async Task Design_module_writes_answer_the_values()
    {
        var graph = await SeedGraph();

        var created = await ReadAsync<JsonElement>(await RootClient.PostAsJsonAsync("api/designModules", new
        {
            designId = graph.Design.Id,
            moduleId = graph.Module.Id,
            name = "created",
            moduleVersion = "1.0.0",
            values = new[] { new { name = "count", value = "5" } }
        }, Ct));
        var put = await ReadAsync<JsonElement>(await RootClient.PutAsJsonAsync($"api/designModules/{graph.DesignModule.Id}", new
        {
            moduleId = graph.Module.Id,
            name = "edited",
            moduleVersion = "1.0.0",
            values = new[] { new { name = "size", value = "large" } }
        }, Ct));
        var added = await ReadAsync<JsonElement>(await RootClient.PostAsJsonAsync($"api/designModules/{graph.DesignModule.Id}/values",
            new { values = new[] { new { name = "zone", value = "a" } } }, Ct));
        var disabled = await ReadAsync<JsonElement>(await RootClient.PostAsync($"api/designModules/{graph.DesignModule.Id}/actions/disable", null, Ct));

        AssertValues(created, ("count", "5"));
        Assert.Equal("edited", put.GetProperty("name").GetString());
        AssertValues(put, ("count", "3"), ("size", "large"));
        AssertValues(added, ("count", "3"), ("size", "large"), ("zone", "a"));
        Assert.False(disabled.GetProperty("enabled").GetBoolean());
        AssertValues(disabled, ("count", "3"), ("size", "large"), ("zone", "a"));
        var broadcast = Assert.IsType<DesignModuleView>(Assert.Single(Factory.Hub<ProjectHub>().ToGroup(graph.Design.Id), x => x.Method == "DesignModuleCreated").Arguments[0]);
        Assert.Equal(("count", "5"), (Assert.Single(broadcast.Values).Name, broadcast.Values.Single().Value));
    }

    // ---- Variables --------------------------------------------------------------------------------

    /// <summary>A variable is answered with its Terraform reference, on reads, writes and the broadcast.</summary>
    [Fact]
    public async Task A_variable_is_answered_with_its_terraform_reference()
    {
        var graph = await SeedGraph();

        var one = await ReadAsync<JsonElement>(await RootClient.GetAsync($"api/variables/{graph.Variable.Id}", Ct));
        var list = await ReadAsync<JsonElement[]>(await RootClient.GetAsync($"api/designs/{graph.Design.Id}/variables", Ct));
        var created = await ReadAsync<JsonElement>(await RootClient.PostAsJsonAsync("api/variables",
            new { designId = graph.Design.Id, name = "created", type = "number", defaultValue = "1" }, Ct));
        var put = await ReadAsync<JsonElement>(await RootClient.PutAsJsonAsync($"api/variables/{graph.Variable.Id}",
            new { name = "renamed", type = "bool" }, Ct));

        foreach (var json in new[] { one, Assert.Single(list) })
        {
            Assert.Equal(graph.Variable.Id, json.GetProperty("id").GetGuid());
            Assert.Equal(graph.Design.Id, json.GetProperty("designId").GetGuid());
            Assert.Equal("test_variable", json.GetProperty("name").GetString());
            Assert.Equal("string", json.GetProperty("type").GetString());
            Assert.Equal("value", json.GetProperty("defaultValue").GetString());
            Assert.Equal("${var.test_variable}", json.GetProperty("terraform").GetString());
        }
        Assert.Equal(("number", "1", "${var.created}"),
            (created.GetProperty("type").GetString(), created.GetProperty("defaultValue").GetString(), created.GetProperty("terraform").GetString()));
        Assert.Equal(("bool", JsonValueKind.Null, "${var.renamed}"),
            (put.GetProperty("type").GetString(), put.GetProperty("defaultValue").ValueKind, put.GetProperty("terraform").GetString()));
        Assert.Null((await ReadBack(db => db.Variables.SingleAsync(x => x.Id == graph.Variable.Id, Ct))).DefaultValue);
        var broadcast = Assert.IsType<VariableView>(Assert.Single(Factory.Hub<ProjectHub>().ToGroup(graph.Design.Id), x => x.Method == "VariableCreated").Arguments[0]);
        Assert.Equal("${var.created}", broadcast.Terraform);
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    private static void AssertDesign(JsonElement json, Design design)
    {
        Assert.Equal(design.Id, json.GetProperty("id").GetGuid());
        Assert.Equal(design.Name, json.GetProperty("name").GetString());
        Assert.Equal(design.DirectoryId, json.GetProperty("directoryId").GetGuid());
        Assert.False(json.GetProperty("enabled").GetBoolean());
    }

    private static void AssertDesignModule(JsonElement json, Graph graph)
    {
        Assert.Equal(graph.DesignModule.Id, json.GetProperty("id").GetGuid());
        Assert.Equal(graph.Design.Id, json.GetProperty("designId").GetGuid());
        Assert.Equal(graph.Module.Id, json.GetProperty("moduleId").GetGuid());
        Assert.Equal("test-design-module", json.GetProperty("name").GetString());
        Assert.Equal("1.0.0", json.GetProperty("moduleVersion").GetString());
        Assert.True(json.GetProperty("enabled").GetBoolean());
    }

    private static void AssertValues(JsonElement json, params (string Name, string Value)[] expected) =>
        Assert.Equal(expected, json.GetProperty("values").EnumerateArray()
            .Select(x => (x.GetProperty("name").GetString(), x.GetProperty("value").GetString())).ToArray());

    private sealed record Graph(Project Project, Design Design, Module Module, DesignModule DesignModule, Variable Variable);

    /// <summary>A disabled design with one module (one value) and one variable; the module has no variables, so any values are complete.</summary>
    private async Task<Graph> SeedGraph()
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        var design = TestData.Design(directory);
        design.Enabled = false;
        var module = TestData.Module();
        var designModule = TestData.DesignModule(design, module);
        designModule.Values = [new ModuleValue { Name = "count", Value = "3" }];
        var variable = TestData.Variable(design);
        await Seed(project, directory, design, module, TestData.ModuleVersion(module), designModule, variable);

        return new Graph(project, design, module, designModule, variable);
    }
}
