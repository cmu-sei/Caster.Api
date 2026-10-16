// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Serialization;
using File = System.IO.File;

namespace Caster.Api.Tests.Domain.Models;

/// <summary>Parsing <c>terraform show -json</c> plan output (<c>Data/plan.json</c>) into <see cref="PlanOutput"/>.</summary>
public class PlanOutputTests
{
    private static readonly Lazy<PlanOutput> Plan = new(() => JsonSerializer.Deserialize<PlanOutput>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "plan.json")),
        DefaultJsonSettings.Settings));

    [Fact]
    public void The_plan_lists_every_resource_change()
    {
        Assert.Equal(10, Plan.Value.ResourceChanges.Count());
    }

    [Fact]
    public void GetAddedMachines_counts_the_virtual_machines_being_created()
    {
        Assert.Equal(7, Plan.Value.GetAddedMachines().Count());
    }
}
