// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.ModuleScopes;
using Caster.Api.Infrastructure.Options;
using Microsoft.Extensions.Configuration;

namespace Caster.Api.Tests.Domain.Services.ModuleScopes;

/// <summary>What a project's access allows with shared sources, how accesses unite, and how the settings are read.</summary>
[Trait("Category", "Unit")]
public class ModuleAccessTests
{
    private static string[] Visible(ModuleAccess access, params string[] paths) => paths.Where(access.Allows).ToArray();

    [Fact]
    public void Everything_allows_every_path_and_has_no_shared_sources() =>
        Assert.Equal(["cmu/a", "sei/b", "stock/c"], Visible(ModuleAccess.Everything, "cmu/a", "sei/b", "stock/c"));

    [Fact]
    public void A_restricted_access_allows_its_paths_and_the_shared_sources_only() =>
        Assert.Equal(["cmu/a", "stock/c"], Visible(ModuleAccess.Of(["cmu"], ["stock"]), "cmu/a", "sei/b", "stock/c"));

    [Fact]
    public void With_no_paths_only_the_shared_sources_are_allowed() =>
        Assert.Equal(["stock/c", "stock"], Visible(ModuleAccess.Of([], ["stock"]), "cmu/a", "stock/c", "stock"));

    [Fact]
    public void With_no_paths_and_no_shared_sources_nothing_is_allowed() =>
        Assert.Empty(Visible(ModuleAccess.Of([], []), "cmu/a", "stock/c"));

    [Fact]
    public void A_shared_source_matches_whole_names_not_prefixes() =>
        Assert.Empty(Visible(ModuleAccess.Of([], ["stock"]), "stock2/c", "stockroom"));

    [Fact]
    public void The_paths_do_not_include_the_shared_sources()
    {
        var access = ModuleAccess.Of(["sei", "cmu", "cmu"], ["stock"]);

        Assert.Equal(["cmu", "sei"], access.Paths);
        Assert.Equal(["stock"], access.SharedSources);
    }

    [Fact]
    public void The_union_of_restricted_accesses_unites_paths_and_keeps_the_shared_sources()
    {
        var union = ModuleAccess.Union([ModuleAccess.Of(["cmu"], ["stock"]), ModuleAccess.Of([], ["stock"]), ModuleAccess.Of(["sei"], ["stock"])]);

        Assert.True(union.Restricted);
        Assert.Equal(["cmu", "sei"], union.Paths);
        Assert.Equal(["stock"], union.SharedSources);
    }

    [Fact]
    public void The_union_is_everything_when_any_access_is_unrestricted() =>
        Assert.False(ModuleAccess.Union([ModuleAccess.Of(["cmu"], ["stock"]), ModuleAccess.Everything]).Restricted);

    [Fact]
    public void The_union_of_no_accesses_is_the_shared_sources_given()
    {
        var union = ModuleAccess.Union([], ["stock"]);

        Assert.True(union.Restricted);
        Assert.Equal(["stock/c"], Visible(union, "cmu/a", "stock/c"));
    }

    [Fact]
    public void Apply_filters_a_query_by_paths_and_shared_sources()
    {
        var modules = new[] { "cmu/a", "cmu/x/deep", "sei/b", "stock/c" }
            .Select(x => new Module { Path = x }).AsQueryable();

        var kept = ModuleAccess.Of(["cmu/x"], ["stock"]).Apply(modules).Select(x => x.Path).Order().ToArray();

        Assert.Equal(["cmu/x/deep", "stock/c"], kept);
        Assert.Equal(4, ModuleAccess.Everything.Apply(modules).Count());
    }

    // ---- settings --------------------------------------------------------------------------------

    private static ModuleSourceOptions Source(string name, bool shared, string url = "https://git.example.com/x.git") =>
        new() { Name = name, Url = url, Shared = shared };

    [Fact]
    public void The_shared_source_names_are_those_configured_and_shared()
    {
        var options = new TerraformOptions { ModuleSources = [Source("cmu", false), Source("stock", true), Source("sei", false)] };

        Assert.Equal(["stock"], options.SharedSourceNames());
    }

    [Fact]
    public void A_shared_source_with_no_url_or_name_is_ignored_and_a_repeated_name_counts_once()
    {
        var options = new TerraformOptions
        {
            ModuleSources = [Source("stock", true), Source("STOCK", true), Source("half", true, url: ""), Source("", true), null]
        };

        Assert.Equal(["stock"], options.SharedSourceNames());
    }

    [Fact]
    public void There_are_no_shared_sources_by_default()
    {
        Assert.Empty(new TerraformOptions().SharedSourceNames());
        Assert.False(new TerraformOptions().ModuleScopes.RestrictUnassigned);
        Assert.False(new ModuleSourceOptions().Shared);
    }

    /// <summary>
    /// The deployment sets these as environment variables (<c>Terraform__ModuleSources__0__Shared</c>), which the
    /// configuration reads as the same keys with ':'. A prefix keeps the test from touching the real environment.
    /// </summary>
    [Fact]
    public void The_settings_bind_from_configuration_keys()
    {
        var options = Bind(new Dictionary<string, string>
        {
            ["Terraform:ModuleSources:0:Name"] = "cmu",
            ["Terraform:ModuleSources:0:Url"] = "https://git.example.com/cmu.git",
            ["Terraform:ModuleSources:1:Name"] = "stock",
            ["Terraform:ModuleSources:1:Url"] = "https://git.example.com/stock.git",
            ["Terraform:ModuleSources:1:Shared"] = "true",
            ["Terraform:ModuleScopes:RestrictUnassigned"] = "true",
        });

        Assert.True(options.ModuleScopes.RestrictUnassigned);
        Assert.Equal(["stock"], options.SharedSourceNames());
        Assert.False(options.ModuleSources[0].Shared);
    }

    [Fact]
    public void The_settings_bind_from_environment_variables()
    {
        const string prefix = "CASTERTEST_MODULESCOPES_";
        var names = new Dictionary<string, string>
        {
            [$"{prefix}Terraform__ModuleSources__0__Name"] = "stock",
            [$"{prefix}Terraform__ModuleSources__0__Url"] = "https://git.example.com/stock.git",
            [$"{prefix}Terraform__ModuleSources__0__Shared"] = "True",
            [$"{prefix}Terraform__ModuleScopes__RestrictUnassigned"] = "true",
        };

        foreach (var (key, value) in names)
            System.Environment.SetEnvironmentVariable(key, value);

        try
        {
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables(prefix).Build();
            var options = configuration.GetSection("Terraform").Get<TerraformOptions>();

            Assert.True(options.ModuleScopes.RestrictUnassigned);
            Assert.Equal(["stock"], options.SharedSourceNames());
        }
        finally
        {
            foreach (var key in names.Keys)
                System.Environment.SetEnvironmentVariable(key, null);
        }
    }

    private static TerraformOptions Bind(Dictionary<string, string> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetSection("Terraform").Get<TerraformOptions>();
}
