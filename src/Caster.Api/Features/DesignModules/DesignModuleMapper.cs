// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Riok.Mapperly.Abstractions;

namespace Caster.Api.Features.DesignModules;

/// <summary>Design modules' in-memory, create and PUT mappings.</summary>
/// <remarks>
/// <para>
/// A design module's values are stored as JSON in the entity's private <c>ValuesJson</c>, which the view
/// model copies and reads <see cref="DesignModule.Values"/> from. This mapper includes non-public members
/// so Mapperly copies it through an <c>UnsafeAccessor</c>, as AutoMapper's profile-wide
/// <c>ShouldMapProperty</c> did. A read projects instead: <see cref="DesignModuleProjectionMapper"/>
/// leaves the values out, <see cref="DesignModuleWithValuesProjectionMapper"/> reads them.
/// </para>
/// <para>
/// A create or edit without values throws (a 500), as it did through AutoMapper: <see cref="ModuleValue.ToDomain(IEnumerable{ModuleValue})"/>
/// does not accept null.
/// </para>
/// </remarks>
[Mapper(IncludedMembers = MemberVisibility.All)]
public static partial class DesignModuleMapper
{
    // The commands are records, whose compiler-generated protected EqualityContract the non-public
    // members this mapper includes would otherwise report as unmapped.
    private const string RecordEqualityContract = "EqualityContract";

    [MapperIgnoreTarget(nameof(DesignModule.Values))]
    [MapperIgnoreTarget(nameof(DesignModule.Placeholders))]
    public static partial DesignModule ToDesignModule(this Domain.Models.DesignModule source);

    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(RecordEqualityContract)]
    [MapProperty(nameof(Create.Command.Values), nameof(Domain.Models.DesignModule.Values), Use = nameof(ToDomainValues))]
    public static partial Domain.Models.DesignModule ToEntity(this Create.Command source);

    /// <summary>
    /// The PUT update. The values are added to, updated in or removed from the stored ones, not replaced.
    /// The merged list is written back, an empty one when the module had none stored, as AutoMapper did
    /// with the null that <see cref="Domain.Models.DesignModule.AddOrUpdateValues"/> returns then.
    /// </summary>
    public static void ApplyTo(this Edit.Command source, Domain.Models.DesignModule target)
    {
        ApplyMembers(source, target);
        target.Values = target.AddOrUpdateValues(ModuleValue.ToDomain(source.Values)) ?? [];
    }

    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(RecordEqualityContract)]
    [MapperIgnoreSource(nameof(Edit.Command.DesignModuleId))]
    [MapperIgnoreSource(nameof(Edit.Command.Values))]
    private static partial void ApplyMembers(Edit.Command source, Domain.Models.DesignModule target);

    private static ICollection<Domain.Models.ModuleValue> ToDomainValues(ModuleValue[] values) => ModuleValue.ToDomain(values);
}

/// <summary>The projection of a design module without its values, which are left null.</summary>
[Mapper]
public static partial class DesignModuleProjectionMapper
{
    public static partial IQueryable<DesignModule> ProjectToDesignModule(this IQueryable<Domain.Models.DesignModule> query);

    [MapperIgnoreTarget(nameof(DesignModule.Values))]
    [MapperIgnoreTarget(nameof(DesignModule.Placeholders))]
    [MapperIgnoreTarget(nameof(DesignModule.ValuesJson))]
    private static partial DesignModule MapToDesignModule(Domain.Models.DesignModule source);
}

/// <summary>The projection of a design module with its values.</summary>
[Mapper]
public static partial class DesignModuleWithValuesProjectionMapper
{
    public static partial IQueryable<DesignModule> ProjectToDesignModuleWithValues(this IQueryable<Domain.Models.DesignModule> query);

    [MapperIgnoreTarget(nameof(DesignModule.Values))]
    [MapperIgnoreTarget(nameof(DesignModule.Placeholders))]
    [MapPropertyFromSource(nameof(DesignModule.ValuesJson), Use = nameof(ReadValuesJson))]
    private static partial DesignModule MapToDesignModule(Domain.Models.DesignModule source);

    // The entity's ValuesJson is private: EF.Property reads its column inside the query.
    private static string ReadValuesJson(Domain.Models.DesignModule source) => EF.Property<string>(source, "ValuesJson");
}
