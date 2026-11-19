// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using Riok.Mapperly.Abstractions;

namespace Caster.Api.Features.Workspaces;

/// <summary>Workspaces' mappings, apart from PATCH, which is <see cref="WorkspacePatchMapper"/>.</summary>
[Mapper]
public static partial class WorkspaceMapper
{
    public static partial IQueryable<Workspace> ProjectToWorkspace(this IQueryable<Domain.Models.Workspace> query);

    public static partial Workspace ToWorkspace(this Domain.Models.Workspace source);

    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    public static partial Domain.Models.Workspace ToEntity(this Create.Command source);

    /// <summary>The PUT update: every member is written, so a null version, parallelism or threshold clears it.</summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(Edit.Command.Id))]
    public static partial void ApplyTo(this Edit.Command source, Domain.Models.Workspace target);
}

/// <summary>The PATCH update. Every null in the command is skipped, so an omitted member keeps its current value.</summary>
[Mapper(AllowNullPropertyAssignment = false)]
public static partial class WorkspacePatchMapper
{
    /// <remarks>
    /// Parallelism and the destroy failure threshold are optional values, mapped by hand: a value is
    /// written only when the command carries one that is not null, so an explicit null is skipped like an
    /// omitted member, as AutoMapper's condition did, and neither can be cleared by a PATCH.
    /// </remarks>
    public static void ApplyPatchTo(this PartialEdit.Command source, Domain.Models.Workspace target)
    {
        ApplyNonNullMembers(source, target);

        if (source.Parallelism.HasValue && source.Parallelism.Value.HasValue)
            target.Parallelism = source.Parallelism.Value;

        if (source.AzureDestroyFailureThreshold.HasValue && source.AzureDestroyFailureThreshold.Value.HasValue)
            target.AzureDestroyFailureThreshold = source.AzureDestroyFailureThreshold.Value;
    }

    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(PartialEdit.Command.Id))]
    [MapperIgnoreSource(nameof(PartialEdit.Command.Parallelism))]
    [MapperIgnoreSource(nameof(PartialEdit.Command.AzureDestroyFailureThreshold))]
    private static partial void ApplyNonNullMembers(PartialEdit.Command source, Domain.Models.Workspace target);
}
