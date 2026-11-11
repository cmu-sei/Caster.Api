// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using Riok.Mapperly.Abstractions;

namespace Caster.Api.Features.Hosts;

/// <summary>Hosts' mappings, apart from the partial edit, which is <see cref="HostPatchMapper"/>.</summary>
[Mapper]
public static partial class HostMapper
{
    public static partial IQueryable<Host> ProjectToHost(this IQueryable<Domain.Models.Host> query);

    public static partial Host ToHost(this Domain.Models.Host source);

    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    public static partial Domain.Models.Host ToEntity(this Create.Command source);

    /// <summary>The PUT update: every member is written, so an omitted one is cleared or zeroed.</summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(Edit.Command.Id))]
    public static partial void ApplyTo(this Edit.Command source, Domain.Models.Host target);
}

/// <summary>The partial edit. Every null in the command is skipped, so an omitted member keeps its current value.</summary>
[Mapper(AllowNullPropertyAssignment = false)]
public static partial class HostPatchMapper
{
    /// <remarks>
    /// The project is an optional value, mapped by hand: it is written only when the command carries one
    /// that is not null, so an explicit null is skipped like an omitted member, as AutoMapper's condition did.
    /// </remarks>
    public static void ApplyPatchTo(this PartialEdit.Command source, Domain.Models.Host target)
    {
        ApplyNonNullMembers(source, target);

        if (source.ProjectId.HasValue && source.ProjectId.Value.HasValue)
            target.ProjectId = source.ProjectId.Value;
    }

    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(PartialEdit.Command.Id))]
    [MapperIgnoreSource(nameof(PartialEdit.Command.ProjectId))]
    private static partial void ApplyNonNullMembers(PartialEdit.Command source, Domain.Models.Host target);
}
