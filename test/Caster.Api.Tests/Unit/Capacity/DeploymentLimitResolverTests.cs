// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using Caster.Api.Domain.Services.Capacity;
using Caster.Api.Infrastructure.Options;
using Microsoft.Extensions.Configuration;

namespace Caster.Api.Tests.Unit.Capacity;

[Trait("Category", "Unit")]
public class DeploymentLimitResolverTests
{
    private sealed record Limits(int? MaxMachines = null, int? MaxVcpu = null, int? MaxMemoryGiB = null, int? MaxDiskGiB = null) : IDeploymentLimits;

    [Fact]
    public void The_lowest_of_global_project_and_workspace_wins()
    {
        var limits = DeploymentLimitResolver.Resolve(
            new CapacityOptions { MaxMachines = 50, MaxVcpu = 100, MaxMemoryGiB = 64 },
            new Limits(MaxMachines: 20, MaxVcpu: 200),
            new Limits(MaxMachines: 30, MaxMemoryGiB: 32));

        Assert.Equal((20, LimitSource.Project), (limits.MaxMachines.Value, limits.MaxMachines.Source));
        Assert.Equal((100, LimitSource.Global), (limits.MaxVcpu.Value, limits.MaxVcpu.Source));
        Assert.Equal((32, LimitSource.Workspace), (limits.MaxMemoryGiB.Value, limits.MaxMemoryGiB.Source));
    }

    [Fact]
    public void A_limit_none_sets_is_no_limit()
    {
        var limits = DeploymentLimitResolver.Resolve(new CapacityOptions(), new Limits(), null);

        Assert.Null(limits.MaxMachines);
        Assert.Null(limits.MaxVcpu);
        Assert.Null(limits.MaxMemoryGiB);
        Assert.Null(limits.MaxDiskGiB);
        Assert.False(limits.Any);
    }

    [Fact]
    public void On_a_tie_the_narrowest_level_is_named()
    {
        var limits = DeploymentLimitResolver.Resolve(new CapacityOptions { MaxDiskGiB = 500 }, new Limits(MaxDiskGiB: 500), new Limits(MaxDiskGiB: 500));

        Assert.Equal(LimitSource.Workspace, limits.MaxDiskGiB.Source);
    }

    [Fact]
    public void Zero_is_a_limit()
    {
        var limits = DeploymentLimitResolver.Resolve(new CapacityOptions { MaxMachines = 10 }, null, new Limits(MaxMachines: 0));

        Assert.Equal(0, limits.MaxMachines.Value);
    }

    [Fact]
    public void The_shipped_settings_bind_to_no_limits_and_the_defaults()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(System.IO.Path.Combine(System.AppContext.BaseDirectory, "appsettings.json"), optional: false)
            .Build();

        var options = new CapacityOptions();
        configuration.GetSection("Capacity").Bind(options);

        Assert.Null(options.MaxMachines);
        Assert.Null(options.MaxVcpu);
        Assert.Null(options.MaxMemoryGiB);
        Assert.Null(options.MaxDiskGiB);
        Assert.Equal(4, options.CpuOvercommitRatio);
        Assert.Equal(10, options.ReservePercent);
        Assert.Equal(RoomCheckMode.Warn, options.RoomCheck);
    }

    [Fact]
    public void Empty_values_bind_to_no_limit()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> { ["Capacity:MaxVcpu"] = "", ["Capacity:MaxMachines"] = "12", ["Capacity:RoomCheck"] = "Block" })
            .Build();

        var options = new CapacityOptions();
        configuration.GetSection("Capacity").Bind(options);

        Assert.Null(options.MaxVcpu);
        Assert.Equal(12, options.MaxMachines);
        Assert.Equal(RoomCheckMode.Block, options.RoomCheck);
    }
}
