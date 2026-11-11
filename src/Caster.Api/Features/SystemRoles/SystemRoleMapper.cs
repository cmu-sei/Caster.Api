// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Caster.Api.Domain.Models;
using Riok.Mapperly.Abstractions;

namespace Caster.Api.Features.SystemRoles;

/// <summary>System roles' in-memory, create and PUT mappings. A missing permission list is stored and answered as an empty one.</summary>
[Mapper]
public static partial class SystemRoleMapper
{
    public static partial SystemRole ToSystemRole(this Domain.Models.SystemRole source);

    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    public static partial Domain.Models.SystemRole ToEntity(this Create.Command source);

    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(Edit.Command.Id))]
    public static partial void ApplyTo(this Edit.Command source, Domain.Models.SystemRole target);

    private static SystemPermission[] MapPermissions(List<SystemPermission> permissions) =>
        permissions == null ? [] : permissions.ToArray();

    private static List<SystemPermission> MapPermissions(SystemPermission[] permissions) =>
        permissions == null ? [] : permissions.ToList();
}

/// <summary>The projection of a system role.</summary>
[Mapper]
public static partial class SystemRoleProjectionMapper
{
    public static partial IQueryable<SystemRole> ProjectToSystemRole(this IQueryable<Domain.Models.SystemRole> query);

    // The Select makes EF Core read the permissions element by element (an unnest of the array column),
    // as AutoMapper's projection did, so a role stored without permissions is read with an empty list.
    // Without it the column is read as is, and a null array stays null.
    private static SystemPermission[] MapPermissions(List<SystemPermission> permissions) => permissions.Select(x => x).ToArray();
}
