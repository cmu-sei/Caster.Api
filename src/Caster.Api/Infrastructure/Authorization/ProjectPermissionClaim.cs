/*
Copyright 2021 Carnegie Mellon University. All Rights Reserved. 
 Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
*/

using System;
using System.Text.Json;
using Caster.Api.Domain.Models;

namespace Caster.Api.Infrastructure.Authorization;

public class ProjectPermissionsClaim
{
    public Guid ProjectId { get; set; }
    public ProjectPermission[] Permissions { get; set; } = [];

    public ProjectPermissionsClaim() { }

    public static ProjectPermissionsClaim FromString(string json)
    {
        return JsonSerializer.Deserialize<ProjectPermissionsClaim>(json);
    }

    public override string ToString()
    {
        return JsonSerializer.Serialize(this);
    }
}