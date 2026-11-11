// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Caster.Api.Features.Applies;
using Caster.Api.Features.Plans;
using Riok.Mapperly.Abstractions;

namespace Caster.Api.Features.Runs;

/// <summary>Runs' command and queue position mappings. A run is only answered through a projection: see <see cref="RunsExtensions.Expand"/>.</summary>
[Mapper]
public static partial class RunMapper
{
    /// <summary>A new run from the create command. A missing target or replace list is stored as an empty one, as AutoMapper did.</summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapProperty(nameof(Create.Command.Targets), nameof(Domain.Models.Run.Targets), Use = nameof(MapAddresses))]
    [MapProperty(nameof(Create.Command.ReplaceAddresses), nameof(Domain.Models.Run.ReplaceAddresses), Use = nameof(MapAddresses))]
    public static partial Domain.Models.Run ToEntity(this Create.Command source);

    public static partial QueuePosition ToQueuePosition(this Domain.Services.QueuePosition source);

    public static List<QueuePosition> ToQueuePositions(this IEnumerable<Domain.Services.QueuePosition> source) =>
        source.Select(x => x.ToQueuePosition()).ToList();

    [UserMapping(Default = false)]
    private static string[] MapAddresses(string[] addresses) => addresses ?? [];
}

// Each projection variant is a mapper of its own, because a projection takes the default mapping of
// its element types and a mapper has one default per type pair. The members read through a
// navigation (the plan and apply ids, the creator's and modifier's names) are expression-bodied
// helpers, which Mapperly inlines without a null guard, so the SQL is the LEFT JOIN AutoMapper's was.

/// <summary>The projection of a run without its plan and apply (their ids are always read).</summary>
[Mapper]
public static partial class RunProjectionMapper
{
    public static partial IQueryable<Run> ProjectToRun(this IQueryable<Domain.Models.Run> query);

    [MapperIgnoreTarget(nameof(Run.Plan))]
    [MapperIgnoreTarget(nameof(Run.Apply))]
    [MapProperty(nameof(Domain.Models.Run.Plan), nameof(Run.PlanId), Use = nameof(MapPlanId))]
    [MapProperty(nameof(Domain.Models.Run.Apply), nameof(Run.ApplyId), Use = nameof(MapApplyId))]
    [MapProperty(nameof(Domain.Models.Run.CreatedBy), nameof(Run.CreatedBy), Use = nameof(MapUserName))]
    [MapProperty(nameof(Domain.Models.Run.ModifiedBy), nameof(Run.ModifiedBy), Use = nameof(MapUserName))]
    private static partial Run MapToRun(Domain.Models.Run source);

    private static Guid? MapPlanId(Domain.Models.Plan plan) => plan == null ? null : plan.Id;

    private static Guid? MapApplyId(Domain.Models.Apply apply) => apply == null ? null : apply.Id;

    private static string MapUserName(Domain.Models.User user) => user.Name;
}

/// <summary>The projection of a run with its plan.</summary>
[Mapper]
public static partial class RunWithPlanProjectionMapper
{
    public static partial IQueryable<Run> ProjectToRunWithPlan(this IQueryable<Domain.Models.Run> query);

    [MapperIgnoreTarget(nameof(Run.Apply))]
    [MapProperty(nameof(Domain.Models.Run.Plan), nameof(Run.PlanId), Use = nameof(MapPlanId))]
    [MapProperty(nameof(Domain.Models.Run.Apply), nameof(Run.ApplyId), Use = nameof(MapApplyId))]
    [MapProperty(nameof(Domain.Models.Run.CreatedBy), nameof(Run.CreatedBy), Use = nameof(MapUserName))]
    [MapProperty(nameof(Domain.Models.Run.ModifiedBy), nameof(Run.ModifiedBy), Use = nameof(MapUserName))]
    private static partial Run MapToRun(Domain.Models.Run source);

    private static partial Plan MapToPlan(Domain.Models.Plan source);

    private static Guid? MapPlanId(Domain.Models.Plan plan) => plan == null ? null : plan.Id;

    private static Guid? MapApplyId(Domain.Models.Apply apply) => apply == null ? null : apply.Id;

    private static string MapUserName(Domain.Models.User user) => user.Name;
}

/// <summary>The projection of a run with its apply.</summary>
[Mapper]
public static partial class RunWithApplyProjectionMapper
{
    public static partial IQueryable<Run> ProjectToRunWithApply(this IQueryable<Domain.Models.Run> query);

    [MapperIgnoreTarget(nameof(Run.Plan))]
    [MapProperty(nameof(Domain.Models.Run.Plan), nameof(Run.PlanId), Use = nameof(MapPlanId))]
    [MapProperty(nameof(Domain.Models.Run.Apply), nameof(Run.ApplyId), Use = nameof(MapApplyId))]
    [MapProperty(nameof(Domain.Models.Run.CreatedBy), nameof(Run.CreatedBy), Use = nameof(MapUserName))]
    [MapProperty(nameof(Domain.Models.Run.ModifiedBy), nameof(Run.ModifiedBy), Use = nameof(MapUserName))]
    private static partial Run MapToRun(Domain.Models.Run source);

    private static partial Apply MapToApply(Domain.Models.Apply source);

    private static Guid? MapPlanId(Domain.Models.Plan plan) => plan == null ? null : plan.Id;

    private static Guid? MapApplyId(Domain.Models.Apply apply) => apply == null ? null : apply.Id;

    private static string MapUserName(Domain.Models.User user) => user.Name;
}

/// <summary>The projection of a run with its plan and its apply.</summary>
[Mapper]
public static partial class RunWithPlanAndApplyProjectionMapper
{
    public static partial IQueryable<Run> ProjectToRunWithPlanAndApply(this IQueryable<Domain.Models.Run> query);

    [MapProperty(nameof(Domain.Models.Run.Plan), nameof(Run.PlanId), Use = nameof(MapPlanId))]
    [MapProperty(nameof(Domain.Models.Run.Apply), nameof(Run.ApplyId), Use = nameof(MapApplyId))]
    [MapProperty(nameof(Domain.Models.Run.CreatedBy), nameof(Run.CreatedBy), Use = nameof(MapUserName))]
    [MapProperty(nameof(Domain.Models.Run.ModifiedBy), nameof(Run.ModifiedBy), Use = nameof(MapUserName))]
    private static partial Run MapToRun(Domain.Models.Run source);

    private static partial Plan MapToPlan(Domain.Models.Plan source);

    private static partial Apply MapToApply(Domain.Models.Apply source);

    private static Guid? MapPlanId(Domain.Models.Plan plan) => plan == null ? null : plan.Id;

    private static Guid? MapApplyId(Domain.Models.Apply apply) => apply == null ? null : apply.Id;

    private static string MapUserName(Domain.Models.User user) => user.Name;
}
