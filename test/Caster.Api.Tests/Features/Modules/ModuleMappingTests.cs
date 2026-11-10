// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Tests.Features.Modules;

/// <summary>
/// What a module is answered with: a single read carries its versions (with their variables and outputs)
/// and their count; the list carries neither, apart from the count when asked for it.
/// </summary>
public class ModuleMappingTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    [Fact]
    public async Task A_module_read_carries_its_versions_their_variables_and_outputs_and_their_count()
    {
        var (module, version) = await SeedModule();

        var json = await ReadAsync<JsonElement>(await RootClient.GetAsync($"api/modules/{module.Id}", Ct));

        AssertModule(json, module);
        Assert.Equal(2, json.GetProperty("versionsCount").GetInt32());
        var versions = json.GetProperty("versions").EnumerateArray().ToArray();
        Assert.Equal(["2.0.0", "1.0.0"], versions.Select(x => x.GetProperty("name").GetString()));
        var first = versions[1];
        Assert.Equal(version.Id, first.GetProperty("id").GetGuid());
        Assert.Equal(module.Id, first.GetProperty("moduleId").GetGuid());
        Assert.Equal(version.UrlLink, first.GetProperty("urlLink").GetString());
        Assert.Equal(TestData.DefaultDateCreated, first.GetProperty("dateCreated").GetDateTime().ToUniversalTime());
        var variables = first.GetProperty("variables").EnumerateArray().ToArray();
        Assert.Equal(2, variables.Length);
        Assert.Equal(("size", "the size", "string", "small", true), Variable(variables[0]));
        Assert.Equal(("name", "the name", "string", null, false), Variable(variables[1]));
        var output = Assert.Single(first.GetProperty("outputs").EnumerateArray());
        Assert.Equal(("id", "the id"), (output.GetProperty("name").GetString(), output.GetProperty("description").GetString()));
        Assert.Equal(0, versions[0].GetProperty("variables").GetArrayLength());
    }

    /// <summary>A version stored without variables or outputs is answered with empty lists, not nulls.</summary>
    [Fact]
    public async Task A_version_stored_without_variables_or_outputs_answers_empty_lists()
    {
        var module = TestData.Module();
        var version = TestData.ModuleVersion(module);
        version.Variables = null;
        version.Outputs = null;
        await Seed(module, version);
        var stored = await ReadBack(db => db.ModuleVersions.SingleAsync(x => x.Id == version.Id, Ct));
        Assert.Equal((null, null), (stored.Variables, stored.Outputs));

        var json = await ReadAsync<JsonElement>(await RootClient.GetAsync($"api/modules/{module.Id}", Ct));

        var read = Assert.Single(json.GetProperty("versions").EnumerateArray());
        Assert.Equal(JsonValueKind.Array, read.GetProperty("variables").ValueKind);
        Assert.Equal(0, read.GetProperty("variables").GetArrayLength());
        Assert.Equal(JsonValueKind.Array, read.GetProperty("outputs").ValueKind);
        Assert.Equal(0, read.GetProperty("outputs").GetArrayLength());
    }

    /// <summary>The module list leaves the versions null and counts them only with includeVersionCount.</summary>
    [Fact]
    public async Task The_module_list_leaves_the_versions_out_and_counts_them_only_when_asked()
    {
        var (module, _) = await SeedModule();

        var plain = await ReadAsync<JsonElement[]>(await RootClient.GetAsync("api/modules", Ct));
        var counted = await ReadAsync<JsonElement[]>(await RootClient.GetAsync("api/modules?includeVersionCount=true", Ct));

        var listed = Assert.Single(plain);
        AssertModule(listed, module);
        Assert.Equal(JsonValueKind.Null, listed.GetProperty("versions").ValueKind);
        Assert.Equal(JsonValueKind.Null, listed.GetProperty("versionsCount").ValueKind);
        var one = Assert.Single(counted);
        AssertModule(one, module);
        Assert.Equal(JsonValueKind.Null, one.GetProperty("versions").ValueKind);
        Assert.Equal(2, one.GetProperty("versionsCount").GetInt32());
    }

    private static (string, string, string, string, bool) Variable(JsonElement json) =>
        (json.GetProperty("name").GetString(), json.GetProperty("description").GetString(), json.GetProperty("variableType").GetString(),
            json.GetProperty("defaultValue").GetString(), json.GetProperty("isOptional").GetBoolean());

    private static void AssertModule(JsonElement json, Module module)
    {
        Assert.Equal(module.Id, json.GetProperty("id").GetGuid());
        Assert.Equal(module.Name, json.GetProperty("name").GetString());
        Assert.Equal(module.Path, json.GetProperty("path").GetString());
        Assert.Equal("a module", json.GetProperty("description").GetString());
        Assert.Equal(TestData.DefaultDateCreated, json.GetProperty("dateModified").GetDateTime().ToUniversalTime());
    }

    /// <summary>A module with two versions; the older one has an optional and a required variable and one output.</summary>
    private async Task<(Module Module, ModuleVersion Version)> SeedModule()
    {
        var module = TestData.Module();
        module.Description = "a module";
        var version = TestData.ModuleVersion(module);
        version.Variables =
        [
            new ModuleVariable { Name = "size", Description = "the size", VariableType = "string", DefaultValue = "small" },
            new ModuleVariable { Name = "name", Description = "the name", VariableType = "string" }
        ];
        version.Outputs = [new ModuleOutput { Name = "id", Description = "the id" }];
        var newer = TestData.ModuleVersion(module, "2.0.0");
        await Seed(module, version, newer);

        return (module, version);
    }
}
