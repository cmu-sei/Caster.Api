// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Riok.Mapperly.Abstractions;

namespace Caster.Api.Features.Modules;

/// <summary>Modules' in-memory mapping.</summary>
/// <remarks>
/// An in-memory map fills the versions from whatever the context has loaded, and counts them. The module
/// list projects instead: see <see cref="ModuleProjectionMapper"/> and <see cref="ModuleWithVersionCountProjectionMapper"/>.
/// </remarks>
[Mapper]
public static partial class ModuleMapper
{
    [MapProperty(nameof(Domain.Models.Module.Versions), nameof(Module.Versions), Use = nameof(MapVersions))]
    [MapProperty(nameof(Domain.Models.Module.Versions), nameof(Module.VersionsCount), Use = nameof(CountVersions))]
    public static partial Module ToModule(this Domain.Models.Module source);

    [MapProperty(nameof(Domain.Models.ModuleVersion.Variables), nameof(ModuleVersion.Variables), Use = nameof(MapVariables))]
    [MapProperty(nameof(Domain.Models.ModuleVersion.Outputs), nameof(ModuleVersion.Outputs), Use = nameof(MapOutputs))]
    private static partial ModuleVersion MapToModuleVersion(Domain.Models.ModuleVersion source);

    private static partial ModuleVariable MapToModuleVariable(Domain.Models.ModuleVariable source);

    private static partial ModuleOutput MapToModuleOutput(Domain.Models.ModuleOutput source);

    private static List<ModuleVersion> MapVersions(ICollection<Domain.Models.ModuleVersion> versions) =>
        versions == null ? [] : versions.Select(x => MapToModuleVersion(x)).ToList();

    private static int? CountVersions(ICollection<Domain.Models.ModuleVersion> versions) => versions?.Count;

    private static List<ModuleVariable> MapVariables(List<Domain.Models.ModuleVariable> variables) =>
        variables == null ? [] : variables.Select(x => MapToModuleVariable(x)).ToList();

    private static List<ModuleOutput> MapOutputs(List<Domain.Models.ModuleOutput> outputs) =>
        outputs == null ? [] : outputs.Select(x => MapToModuleOutput(x)).ToList();

    /// <summary>
    /// AutoMapper had no map from the create command to a module, so creating one has always failed with a
    /// 500 before anything was stored. Kept as it is.
    /// </summary>
    public static Domain.Models.Module ToEntity(this Create.Command source) =>
        throw new InvalidOperationException($"There is no mapping from {typeof(Create.Command).FullName} to {typeof(Domain.Models.Module).FullName}.");
}

// Each projection variant is a mapper of its own, because a projection takes the default mapping of
// its element types and a mapper has one default per type pair.

/// <summary>The projection of a module without its versions or their count, which are left null.</summary>
[Mapper]
public static partial class ModuleProjectionMapper
{
    public static partial IQueryable<Module> ProjectToModule(this IQueryable<Domain.Models.Module> query);

    [MapperIgnoreTarget(nameof(Module.Versions))]
    [MapperIgnoreTarget(nameof(Module.VersionsCount))]
    private static partial Module MapToModule(Domain.Models.Module source);
}

/// <summary>The projection of a module with the count of its versions, but not the versions.</summary>
[Mapper]
public static partial class ModuleWithVersionCountProjectionMapper
{
    public static partial IQueryable<Module> ProjectToModuleWithVersionCount(this IQueryable<Domain.Models.Module> query);

    [MapperIgnoreTarget(nameof(Module.Versions))]
    [MapProperty(nameof(Domain.Models.Module.Versions), nameof(Module.VersionsCount), Use = nameof(CountVersions))]
    private static partial Module MapToModule(Domain.Models.Module source);

    // Expression-bodied, so Mapperly inlines it into the projection as a COUNT subquery.
    private static int? CountVersions(ICollection<Domain.Models.ModuleVersion> versions) => versions.Count;
}

/// <summary>The projection of a module version, with its variables and outputs.</summary>
/// <remarks>
/// The variables and outputs are JSON columns read through a value converter, which EF Core cannot
/// project element by element, so this projection fails to translate (a 500), as AutoMapper's did.
/// </remarks>
[Mapper]
public static partial class ModuleVersionProjectionMapper
{
    public static partial IQueryable<ModuleVersion> ProjectToModuleVersion(this IQueryable<Domain.Models.ModuleVersion> query);

    [MapProperty(nameof(Domain.Models.ModuleVersion.Variables), nameof(ModuleVersion.Variables), Use = nameof(MapVariables))]
    [MapProperty(nameof(Domain.Models.ModuleVersion.Outputs), nameof(ModuleVersion.Outputs), Use = nameof(MapOutputs))]
    private static partial ModuleVersion MapToModuleVersion(Domain.Models.ModuleVersion source);

    private static partial ModuleVariable MapToModuleVariable(Domain.Models.ModuleVariable source);

    private static partial ModuleOutput MapToModuleOutput(Domain.Models.ModuleOutput source);

    private static List<ModuleVariable> MapVariables(List<Domain.Models.ModuleVariable> variables) =>
        variables.Select(x => MapToModuleVariable(x)).ToList();

    private static List<ModuleOutput> MapOutputs(List<Domain.Models.ModuleOutput> outputs) =>
        outputs.Select(x => MapToModuleOutput(x)).ToList();
}
