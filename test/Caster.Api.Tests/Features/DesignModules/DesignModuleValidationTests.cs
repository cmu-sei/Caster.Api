// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Tests.Support;
using DesignModuleValidation = Caster.Api.Features.DesignModules.DesignModuleValidation;

namespace Caster.Api.Tests.Features.DesignModules;

/// <summary>
/// <c>DesignModuleValidation.EnsureComplete</c>, which Create, Edit and AddOrUpdateValues run before they save a
/// design module, over a module whose one version has a required <c>vm_name</c> and an optional <c>cpus</c>.
/// </summary>
public class DesignModuleValidationTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    private const string SeededVersion = "v1.0.0";

    [Fact]
    public async Task EnsureComplete_Rejects_Missing_Required_Value()
    {
        var (design, module) = await SeedDesignAndModule();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => EnsureComplete(NewDesignModule(design, module, SeededVersion)));

        Assert.Contains("vm_name", string.Join(" ", ex.Errors.Values.SelectMany(x => x)));
    }

    [Fact]
    public async Task EnsureComplete_Rejects_Unknown_Version()
    {
        var (design, module) = await SeedDesignAndModule();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            EnsureComplete(NewDesignModule(design, module, "v9.9.9", new ModuleValue { Name = "vm_name", Value = "web" })));

        Assert.True(ex.Errors.ContainsKey(nameof(DesignModule.ModuleVersion)));
    }

    [Fact]
    public async Task EnsureComplete_Rejects_Name_Already_In_Design()
    {
        var (design, module) = await SeedDesignAndModule();
        await Seed(NewDesignModule(design, module, SeededVersion, new ModuleValue { Name = "vm_name", Value = "web" }));

        var duplicate = NewDesignModule(design, module, SeededVersion, new ModuleValue { Name = "vm_name", Value = "db" });

        var ex = await Assert.ThrowsAsync<ValidationException>(() => EnsureComplete(duplicate));

        Assert.True(ex.Errors.ContainsKey(nameof(DesignModule.Name)));
    }

    /// <summary>On Edit the stored module is checked against the others of its design, not against itself.</summary>
    [Fact]
    public async Task EnsureComplete_Accepts_A_Design_Module_That_Keeps_Its_Own_Name()
    {
        var (design, module) = await SeedDesignAndModule();
        var existing = NewDesignModule(design, module, SeededVersion, new ModuleValue { Name = "vm_name", Value = "web" });
        await Seed(existing);

        await EnsureComplete(existing);
    }

    [Fact]
    public async Task EnsureComplete_Accepts_Required_Values_Set()
    {
        var (design, module) = await SeedDesignAndModule();

        await EnsureComplete(NewDesignModule(design, module, SeededVersion, new ModuleValue { Name = "vm_name", Value = "web" }));
    }

    /// <summary>An enabled design is written into Workspaces, so it may not take a placeholder.</summary>
    [Fact]
    public async Task EnsureComplete_Rejects_A_Placeholder_In_An_Enabled_Design()
    {
        var (design, module) = await SeedDesignAndModule();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            EnsureComplete(NewDesignModule(design, module, SeededVersion, new ModuleValue { Name = "vm_name", Value = "REPLACE_WITH_VM_NAME" })));

        Assert.Contains("vm (vm_name)", string.Join(" ", ex.Errors.Values.SelectMany(x => x)));
    }

    [Fact]
    public async Task EnsureComplete_Accepts_A_Placeholder_In_A_Disabled_Design()
    {
        var (design, module) = await SeedDesignAndModule(enabled: false);

        await EnsureComplete(NewDesignModule(design, module, SeededVersion, new ModuleValue { Name = "vm_name", Value = "REPLACE_WITH_VM_NAME" }));
    }

    [Fact]
    public async Task EnsureNoPlaceholders_Names_Each_Enabled_Module_And_Variable_To_Fill()
    {
        var (design, module) = await SeedDesignAndModule(enabled: false);
        var dc = NewDesignModule(design, module, SeededVersion, new ModuleValue { Name = "vm_name", Value = "REPLACE_WITH_VM_NAME" });
        dc.Name = "dc01";
        var off = NewDesignModule(design, module, SeededVersion, new ModuleValue { Name = "vm_name", Value = "REPLACE_WITH_VM_NAME" });
        off.Name = "spare";
        off.Enabled = false;
        var done = NewDesignModule(design, module, SeededVersion, new ModuleValue { Name = "vm_name", Value = "web" });
        await Seed(dc, off, done);

        await using var db = NewContext();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => DesignModuleValidation.EnsureNoPlaceholders(db, design.Id, Ct));

        Assert.Equal(["Before enabling, replace the placeholder values in dc01 (vm_name)"], ex.Errors["Values"]);
    }

    private async Task EnsureComplete(DesignModule designModule)
    {
        await using var db = NewContext();

        await DesignModuleValidation.EnsureComplete(db, designModule, Ct);
    }

    /// <summary>A design, and a module whose one version requires <c>vm_name</c> and defaults <c>cpus</c>.</summary>
    private async Task<(Design Design, Module Module)> SeedDesignAndModule(bool enabled = true)
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        var design = TestData.Design(directory);
        design.Enabled = enabled;
        var module = TestData.Module();
        var version = TestData.ModuleVersion(module, SeededVersion);
        version.Variables =
        [
            new ModuleVariable { Name = "vm_name", VariableType = "string" },
            new ModuleVariable { Name = "cpus", VariableType = "number", DefaultValue = "2" },
        ];
        await Seed(project, directory, design, module, version);

        return (design, module);
    }

    private static DesignModule NewDesignModule(Design design, Module module, string version, params ModuleValue[] values)
    {
        var designModule = TestData.DesignModule(design, module, "vm");
        designModule.ModuleVersion = version;
        designModule.Values = [.. values];

        return designModule;
    }
}
