// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using Riok.Mapperly.Abstractions;

namespace Caster.Api.Features.Users;

/// <summary>
/// Users' in-memory, create and PUT mappings. The role id is a string in the API and a <c>Guid?</c> in the
/// database. A read projects instead: see <see cref="UserProjectionMapper"/>.
/// </summary>
[Mapper]
public static partial class UserMapper
{
    /// <remarks>A user without a role is answered with a null role id here, where a read answers an empty string.</remarks>
    [MapProperty(nameof(Domain.Models.User.RoleId), nameof(User.RoleId), Use = nameof(FormatRoleId))]
    public static partial User ToUser(this Domain.Models.User source);

    /// <summary>A new user from the create command. The command's AllPermissions has nowhere to go and is not stored.</summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(Create.Command.AllPermissions))]
    [MapProperty(nameof(Create.Command.RoleId), nameof(Domain.Models.User.RoleId), Use = nameof(ParseRoleId))]
    public static partial Domain.Models.User ToEntity(this Create.Command source);

    /// <summary>The PUT update: every member is written, so a null role id clears the user's role.</summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(Edit.Command.Id))]
    [MapProperty(nameof(Edit.Command.RoleId), nameof(Domain.Models.User.RoleId), Use = nameof(ParseRoleId))]
    public static partial void ApplyTo(this Edit.Command source, Domain.Models.User target);

    private static string FormatRoleId(Guid? roleId) => roleId?.ToString();

    /// <remarks>A role id that is not a Guid, an empty string included, throws (a 500), as it did through AutoMapper.</remarks>
    private static Guid? ParseRoleId(string roleId) => roleId == null ? null : Guid.Parse(roleId);
}

/// <summary>The projection of a user.</summary>
[Mapper]
public static partial class UserProjectionMapper
{
    public static partial IQueryable<User> ProjectToUser(this IQueryable<Domain.Models.User> query);

    [MapProperty(nameof(Domain.Models.User.RoleId), nameof(User.RoleId), Use = nameof(FormatRoleId))]
    private static partial User MapToUser(Domain.Models.User source);

    // Inlined into the projection, where EF Core translates a nullable Guid's ToString() to
    // COALESCE(role_id::text, ''), as it did for AutoMapper's: a user without a role reads as "".
    private static string FormatRoleId(Guid? roleId) => roleId.ToString();
}
