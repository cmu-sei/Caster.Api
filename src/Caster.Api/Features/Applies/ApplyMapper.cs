// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using Riok.Mapperly.Abstractions;

namespace Caster.Api.Features.Applies;

[Mapper]
public static partial class ApplyMapper
{
    public static partial IQueryable<Apply> ProjectToApply(this IQueryable<Domain.Models.Apply> query);

    public static partial Apply ToApply(this Domain.Models.Apply source);
}
