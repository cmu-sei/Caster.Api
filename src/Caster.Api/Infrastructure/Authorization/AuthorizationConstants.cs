using System;
using Caster.Api.Domain.Models;

namespace Caster.Api.Infrastructure.Authorization;

public static class AuthorizationConstants
{
    public const string PermissionsClaimType = "Permission";
    public const string ProjectPermissionsClaimType = "ProjectPermission";
}