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
    [InlineData("afp", "afp")]
    [InlineData("  afp/exercise-x/ ", "afp/exercise-x")]
    [InlineData("/afp/", "afp")]
    [InlineData("My Org/team_1.2", "My Org/team_1.2")]
    public void A_path_is_trimmed(string path, string expected) =>
        Assert.Equal(expected, ModuleScopePaths.Normalize(path));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    [InlineData(null)]
    [InlineData("afp//x")]
    [InlineData("afp/../x")]
    [InlineData("./afp")]
    [InlineData("afp/*")]
    [InlineData("af?p")]
    [InlineData("afp\\x")]
    [InlineData("afp/ x")]
    [InlineData("-afp")]
    public void A_path_that_no_source_or_folder_can_have_is_refused(string path) =>
        Assert.Null(ModuleScopePaths.Normalize(path));

    [Fact]
    public void A_path_that_is_too_long_is_refused() =>
        Assert.Null(ModuleScopePaths.Normalize(new string('a', ModuleScopePaths.MaximumLength + 1)));

    [Theory]
    [InlineData("afp", "afp", true)]
    [InlineData("afp", "afp/exercise-x", true)]
    [InlineData("afp", "afp/exercise-x/deeper", true)]
    [InlineData("afp", "afp2", false)]
    [InlineData("afp", "afp2/exercise-x", false)]
    [InlineData("afp/exercise-x", "afp/exercise-x", true)]
    [InlineData("afp/exercise-x", "afp/exercise-x/deeper", true)]
    [InlineData("afp/exercise-x", "afp/exercise-xy", false)]
    [InlineData("afp/exercise-x", "afp", false)]
    [InlineData("afp", "AFP/exercise-x", false)]
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
        string[] entries = ["afp", "navy/team-a"];

        Assert.True(ModuleScopePaths.Matches(entries, "navy/team-a/x"));
        Assert.False(ModuleScopePaths.Matches(entries, "navy/team-b"));
        Assert.False(ModuleScopePaths.Allows(entries).Compile()(new Module { Path = "navy/team-b" }));
        Assert.False(ModuleScopePaths.Matches([], "afp"));
    }

    [Fact]
    public void A_project_with_no_scope_may_use_every_module_and_one_with_a_scope_only_its_paths()
    {
        Assert.True(ModuleAccess.Everything.Allows("anything/at/all"));

        var restricted = ModuleAccess.Of(["afp", "afp", "navy"]);
        Assert.Equal(["afp", "navy"], restricted.Paths);
        Assert.True(restricted.Allows("navy/x"));
        Assert.False(restricted.Allows("army"));
    }

    [Fact]
    public void Several_projects_allow_the_union_unless_one_is_unrestricted()
    {
        var a = ModuleAccess.Of(["afp"]);
        var b = ModuleAccess.Of(["navy"]);

        Assert.Equal(["afp", "navy"], ModuleAccess.Union([a, b]).Paths);
        Assert.False(ModuleAccess.Union([a, ModuleAccess.Everything]).Restricted);
        Assert.False(ModuleAccess.Union([]).Allows("afp"));
    }
}
