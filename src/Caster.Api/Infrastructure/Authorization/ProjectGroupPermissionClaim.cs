// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Text.Json;
using Caster.Api.Domain.Models;

namespace Caster.Api.Infrastructure.Authorization;

/// <summary>What a user may do on one project group, from their memberships of it.</summary>
public class ProjectGroupPermissionsClaim
{
    public Guid ProjectGroupId { get; set; }
    public ProjectGroupPermission[] Permissions { get; set; } = [];

    public static ProjectGroupPermissionsClaim FromString(string json) =>
        JsonSerializer.Deserialize<ProjectGroupPermissionsClaim>(json);

    public override string ToString() => JsonSerializer.Serialize(this);
}
