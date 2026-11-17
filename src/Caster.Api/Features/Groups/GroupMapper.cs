// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using Riok.Mapperly.Abstractions;

namespace Caster.Api.Features.Groups;

[Mapper]
public static partial class GroupMapper
{
    public static partial IQueryable<Group> ProjectToGroup(this IQueryable<Domain.Models.Group> query);

    public static partial Group ToGroup(this Domain.Models.Group source);

    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    public static partial Domain.Models.Group ToEntity(this Create.Command source);

    /// <remarks>The handler finds the group by the body's id, so the id is not written.</remarks>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(Edit.Command.Id))]
    public static partial void ApplyTo(this Edit.Command source, Domain.Models.Group target);

    public static partial IQueryable<GroupMembership> ProjectToGroupMembership(this IQueryable<Domain.Models.GroupMembership> query);

    public static partial GroupMembership ToGroupMembership(this Domain.Models.GroupMembership source);
}
