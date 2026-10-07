// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// Mirrors Startup's AddAutoMapper call: the profiles in the Caster.Api assembly, and the one global
// convention, which maps a null Nullable<T> source onto a T destination by keeping the destination's value.
// The resolver it uses (Caster.Api.Infrastructure.Mapping.IgnoreNullSourceValues) is internal, so it is
// copied here; Infrastructure/Mapping/MappingConfigurationTests guards the copy.

using System;
using AutoMapper;
using AutoMapper.Internal;

namespace Caster.Api.Tests.Support;

/// <summary>The application's real AutoMapper configuration, built without starting the application.</summary>
public static class TestMapper
{
    private static readonly Lazy<MapperConfiguration> LazyConfiguration = new(() =>
        new MapperConfiguration(cfg =>
        {
            cfg.AddMaps(typeof(Startup).Assembly);
            cfg.Internal().ForAllPropertyMaps(
                pm => pm.SourceType != null && Nullable.GetUnderlyingType(pm.SourceType) == pm.DestinationType,
                (pm, c) => c.MapFrom<object, object, object, object>(new IgnoreNullSourceValues(), pm.SourceMember.Name));
        }));

    private static readonly Lazy<IMapper> LazyMapper = new(() => LazyConfiguration.Value.CreateMapper());

    /// <summary>The shared configuration, built once for the run.</summary>
    public static MapperConfiguration Configuration => LazyConfiguration.Value;

    /// <summary>A mapper over <see cref="Configuration"/>. Thread-safe; tests share one.</summary>
    public static IMapper Mapper => LazyMapper.Value;

    /// <summary>A copy of the API's internal <c>IgnoreNullSourceValues</c>.</summary>
    private sealed class IgnoreNullSourceValues : IMemberValueResolver<object, object, object, object>
    {
        public object Resolve(
            object source, object destination, object sourceMember, object destinationMember, ResolutionContext context) =>
            sourceMember ?? destinationMember;
    }
}
