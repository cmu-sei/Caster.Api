// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Caster.Api.Domain.Models;
using Riok.Mapperly.Abstractions;

namespace Caster.Api.Features.ProjectRoles;

[Mapper]
public static partial class ProjectRoleMapper
{
    public static partial IQueryable<ProjectRole> ProjectToProjectRole(this IQueryable<Domain.Models.ProjectRole> query);

    // The Select makes EF Core read the permissions element by element (an unnest of the array column),
    // as AutoMapper's projection did, so a role stored without permissions is read with an empty list.
    // Without it the column is read as is, and a null array stays null.
    private static ProjectPermission[] MapPermissions(List<ProjectPermission> permissions) => permissions.Select(x => x).ToArray();
}
