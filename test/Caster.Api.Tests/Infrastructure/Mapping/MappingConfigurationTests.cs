// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using AutoMapper;
using Caster.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using HostEntity = Caster.Api.Domain.Models.Host;
using PartialEditHost = Caster.Api.Features.Hosts.PartialEdit.Command;

namespace Caster.Api.Tests.Infrastructure.Mapping;

/// <summary>
/// Guards <see cref="TestMapper"/>'s copy of the API's internal <c>IgnoreNullSourceValues</c> convention: a
/// null <c>Nullable&lt;T&gt;</c> source keeps the destination's value, in the copy as in the hosted
/// application's own mapper.
/// </summary>
public class MappingConfigurationTests(CasterAppFactory factory)
{
    [Fact]
    public void The_test_mapper_keeps_a_value_a_null_nullable_source_does_not_set()
    {
        var host = new HostEntity { Name = "esx", MaximumMachines = 12, Enabled = true };

        TestMapper.Mapper.Map(new PartialEditHost { Id = Guid.NewGuid(), Name = "esx" }, host);

        Assert.Equal(12, host.MaximumMachines);
        Assert.True(host.Enabled);
    }

    [Fact]
    public void The_applications_mapper_keeps_a_value_a_null_nullable_source_does_not_set()
    {
        var host = new HostEntity { Name = "esx", MaximumMachines = 12, Enabled = true };

        factory.Services.GetRequiredService<IMapper>().Map(new PartialEditHost { Id = Guid.NewGuid(), Name = "esx" }, host);

        Assert.Equal(12, host.MaximumMachines);
        Assert.True(host.Enabled);
    }

    [Fact]
    public void A_set_nullable_source_overwrites_the_destination()
    {
        var host = new HostEntity { Name = "esx", MaximumMachines = 12 };

        TestMapper.Mapper.Map(new PartialEditHost { Id = Guid.NewGuid(), Name = "esx", MaximumMachines = 3 }, host);

        Assert.Equal(3, host.MaximumMachines);
    }
}
