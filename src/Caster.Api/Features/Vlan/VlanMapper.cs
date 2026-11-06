// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Riok.Mapperly.Abstractions;

namespace Caster.Api.Features.Vlan;

/// <summary>Pools', partitions' and VLANs' mappings, apart from PATCH, which is <see cref="VlanPatchMapper"/>.</summary>
[Mapper]
public static partial class VlanMapper
{
    public static partial IQueryable<Vlan> ProjectToVlan(this IQueryable<Domain.Models.Vlan> query);

    public static partial Vlan ToVlan(this Domain.Models.Vlan source);

    public static Vlan[] ToVlans(this IEnumerable<Domain.Models.Vlan> source) => source.Select(x => x.ToVlan()).ToArray();

    public static partial IQueryable<Partition> ProjectToPartition(this IQueryable<Domain.Models.Partition> query);

    public static partial Partition ToPartition(this Domain.Models.Partition source);

    public static partial IQueryable<Pool> ProjectToPool(this IQueryable<Domain.Models.Pool> query);

    public static partial Pool ToPool(this Domain.Models.Pool source);

    /// <summary>A new partition from the create command. The command's VLAN count is not a list of VLANs; the handler adds them.</summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(CreatePartition.Command.Vlans))]
    public static partial Domain.Models.Partition ToEntity(this CreatePartition.Command source);

    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(EditPartition.Command.Id))]
    public static partial void ApplyTo(this EditPartition.Command source, Domain.Models.Partition target);

    /// <summary>A new pool from the create command. The reserved VLAN ids are read by the handler.</summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(CreatePool.Command.ReservedVlanIds))]
    public static partial Domain.Models.Pool ToEntity(this CreatePool.Command source);

    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(EditPool.Command.Id))]
    public static partial void ApplyTo(this EditPool.Command source, Domain.Models.Pool target);
}

/// <summary>The PATCH updates. Every null in a command is skipped, so an omitted member keeps its current value.</summary>
[Mapper(AllowNullPropertyAssignment = false)]
public static partial class VlanPatchMapper
{
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(PartialEditVlan.Command.Id))]
    public static partial void ApplyPatchTo(this PartialEditVlan.Command source, Domain.Models.Vlan target);

    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(PartialEditPartition.Command.Id))]
    public static partial void ApplyPatchTo(this PartialEditPartition.Command source, Domain.Models.Partition target);

    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(PartialEditPool.Command.Id))]
    public static partial void ApplyPatchTo(this PartialEditPool.Command source, Domain.Models.Pool target);
}
