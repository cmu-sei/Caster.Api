// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.ModuleScopes;

namespace Caster.Api.Tests.Domain.Services.ModuleScopes;

/// <summary>What a scope's paths are, and which Module Paths they allow.</summary>
[Trait("Category", "Unit")]
public class ModuleScopePathsTests
{
    [Theory]
    [InlineData("cmu", "cmu")]
    [InlineData("  cmu/exercise-x/ ", "cmu/exercise-x")]
    [InlineData("/cmu/", "cmu")]
    [InlineData("My Org/team_1.2", "My Org/team_1.2")]
    public void A_path_is_trimmed(string path, string expected) =>
        Assert.Equal(expected, ModuleScopePaths.Normalize(path));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    [InlineData(null)]
    [InlineData("cmu//x")]
    [InlineData("cmu/../x")]
    [InlineData("./cmu")]
    [InlineData("cmu/*")]
    [InlineData("af?p")]
    [InlineData("cmu\\x")]
    [InlineData("cmu/ x")]
    [InlineData("-cmu")]
    public void A_path_that_no_source_or_folder_can_have_is_refused(string path) =>
        Assert.Null(ModuleScopePaths.Normalize(path));

    [Fact]
    public void A_path_that_is_too_long_is_refused() =>
        Assert.Null(ModuleScopePaths.Normalize(new string('a', ModuleScopePaths.MaximumLength + 1)));

    [Theory]
    [InlineData("cmu", "cmu", true)]
    [InlineData("cmu", "cmu/exercise-x", true)]
    [InlineData("cmu", "cmu/exercise-x/deeper", true)]
    [InlineData("cmu", "cmu2", false)]
    [InlineData("cmu", "cmu2/exercise-x", false)]
    [InlineData("cmu/exercise-x", "cmu/exercise-x", true)]
    [InlineData("cmu/exercise-x", "cmu/exercise-x/deeper", true)]
    [InlineData("cmu/exercise-x", "cmu/exercise-xy", false)]
    [InlineData("cmu/exercise-x", "cmu", false)]
    [InlineData("cmu", "CMU/exercise-x", false)]
    public void An_entry_allows_its_own_path_and_those_below_it(string entry, string modulePath, bool expected)
    {
        Assert.Equal(expected, ModuleScopePaths.Matches([entry], modulePath));

        // the query condition gives the same answer as the in-memory test
        var module = new Module { Path = modulePath };
        Assert.Equal(expected, ModuleScopePaths.Allows([entry]).Compile()(module));
    }

    [Fact]
    public void Any_entry_is_enough()
    {
        string[] entries = ["cmu", "sei/team-a"];

        Assert.True(ModuleScopePaths.Matches(entries, "sei/team-a/x"));
        Assert.False(ModuleScopePaths.Matches(entries, "sei/team-b"));
        Assert.False(ModuleScopePaths.Allows(entries).Compile()(new Module { Path = "sei/team-b" }));
        Assert.False(ModuleScopePaths.Matches([], "cmu"));
    }

    [Fact]
    public void A_project_with_no_scope_may_use_every_module_and_one_with_a_scope_only_its_paths()
    {
        Assert.True(ModuleAccess.Everything.Allows("anything/at/all"));

        var restricted = ModuleAccess.Of(["cmu", "cmu", "sei"]);
        Assert.Equal(["cmu", "sei"], restricted.Paths);
        Assert.True(restricted.Allows("sei/x"));
        Assert.False(restricted.Allows("cyber"));
    }

    [Fact]
    public void Several_projects_allow_the_union_unless_one_is_unrestricted()
    {
        var a = ModuleAccess.Of(["cmu"]);
        var b = ModuleAccess.Of(["sei"]);

        Assert.Equal(["cmu", "sei"], ModuleAccess.Union([a, b]).Paths);
        Assert.False(ModuleAccess.Union([a, ModuleAccess.Everything]).Restricted);
        Assert.False(ModuleAccess.Union([]).Allows("cmu"));
    }
}
