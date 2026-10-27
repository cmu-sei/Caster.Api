// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Tests.Data;

/// <summary>
/// <c>CasterContext.GetWorkspaceFiles</c>, which a plan and every resource command write into the workspace's
/// working directory: the module version each design module of the directory is written with.
/// </summary>
public class WorkspaceQueriesTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    private const string DesignName = "network";

    private const string DesignModuleName = "web";

    /// <summary>A design module created before a version was saved on create has none, and is written with the highest.</summary>
    [Fact]
    public async Task GetWorkspaceFiles_writes_a_design_module_with_no_saved_version_with_the_highest_version()
    {
        var (workspace, design, module) = await SeedWorkspaceWithADesign();
        var lower = TestData.ModuleVersion(module, "v1.2.0");
        lower.DateCreated = TestData.DefaultDateCreated.AddDays(1);
        var higher = TestData.ModuleVersion(module, "v1.10.0");
        await Seed(lower, higher, DesignModule(design, module, version: null));

        var files = await GetWorkspaceFiles(workspace);

        var designFile = Assert.Single(files, x => x.Name == $"{DesignName}-design.tf");
        Assert.Contains($"source = \"git::{higher.UrlLink}?ref=v1.10.0\"", designFile.Content);
        Assert.DoesNotContain("v1.2.0", designFile.Content);
    }

    [Fact]
    public async Task GetWorkspaceFiles_refuses_a_design_module_whose_saved_version_no_longer_exists()
    {
        var (workspace, design, module) = await SeedWorkspaceWithADesign();
        await Seed(TestData.ModuleVersion(module, "v1.0.0"), DesignModule(design, module, version: "v0.9.0"));

        var ex = await Assert.ThrowsAsync<ConflictException>(() => GetWorkspaceFiles(workspace));

        Assert.Equal($"Module {DesignModuleName} in Design {DesignName} uses version v0.9.0, which no longer exists", ex.Message);
    }

    [Fact]
    public async Task GetWorkspaceFiles_refuses_a_design_module_with_no_saved_version_whose_module_has_no_versions()
    {
        var (workspace, design, module) = await SeedWorkspaceWithADesign();
        await Seed(DesignModule(design, module, version: null));

        var ex = await Assert.ThrowsAsync<ConflictException>(() => GetWorkspaceFiles(workspace));

        Assert.Equal($"Module {DesignModuleName} in Design {DesignName} has no versions to use", ex.Message);
    }

    /// <summary>The workspace and its directory as a plan loads them, read through a context of the call's own.</summary>
    private async Task<List<File>> GetWorkspaceFiles(Workspace seeded)
    {
        await using var db = NewContext();
        var workspace = await db.Workspaces.Include(x => x.Directory).SingleAsync(x => x.Id == seeded.Id, Ct);

        return await db.GetWorkspaceFiles(workspace, workspace.Directory);
    }

    /// <summary>A workspace whose directory holds the design <see cref="DesignName"/>, and a module with no versions yet.</summary>
    private async Task<(Workspace Workspace, Design Design, Module Module)> SeedWorkspaceWithADesign()
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        var workspace = TestData.Workspace(directory);
        var design = TestData.Design(directory, DesignName);
        var module = TestData.Module();
        await Seed(project, directory, workspace, design, module);

        return (workspace, design, module);
    }

    private static DesignModule DesignModule(Design design, Module module, string version)
    {
        var designModule = TestData.DesignModule(design, module, DesignModuleName);
        designModule.ModuleVersion = version;

        return designModule;
    }
}
