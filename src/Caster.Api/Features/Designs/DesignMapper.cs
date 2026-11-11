// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Caster.Api.Features.DesignModules;
using Riok.Mapperly.Abstractions;

namespace Caster.Api.Features.Designs;

/// <summary>Designs' in-memory, create and PUT mappings.</summary>
/// <remarks>
/// An in-memory map fills the modules from whatever the context has loaded, an empty list when nothing
/// is. A read of one design projects instead, without its modules: see <see cref="DesignProjectionMapper"/>.
/// </remarks>
[Mapper]
public static partial class DesignMapper
{
    [MapProperty(nameof(Domain.Models.Design.Modules), nameof(Design.Modules), Use = nameof(MapModules))]
    public static partial Design ToDesign(this Domain.Models.Design source);

    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    public static partial Domain.Models.Design ToEntity(this Create.Command source);

    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(Edit.Command.Id))]
    public static partial void ApplyTo(this Edit.Command source, Domain.Models.Design target);

    private static List<DesignModule> MapModules(ICollection<Domain.Models.DesignModule> modules) =>
        modules == null ? [] : modules.Select(x => x.ToDesignModule()).ToList();
}

/// <summary>The projection of a design without its modules, which are left null.</summary>
[Mapper]
public static partial class DesignProjectionMapper
{
    public static partial IQueryable<Design> ProjectToDesign(this IQueryable<Domain.Models.Design> query);

    [MapperIgnoreTarget(nameof(Design.Modules))]
    private static partial Design MapToDesign(Domain.Models.Design source);
}
