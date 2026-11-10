// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using Riok.Mapperly.Abstractions;

namespace Caster.Api.Features.Plans;

[Mapper]
public static partial class PlanMapper
{
    public static partial IQueryable<Plan> ProjectToPlan(this IQueryable<Domain.Models.Plan> query);
}
