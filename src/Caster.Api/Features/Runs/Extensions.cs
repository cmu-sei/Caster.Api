// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.


using System.Linq;

namespace Caster.Api.Features.Runs
{
    public static class RunsExtensions
    {
        /// <summary>Projects the runs with their plan and their apply when asked for; their ids are always read.</summary>
        public static IQueryable<Run> Expand(this IQueryable<Domain.Models.Run> query, bool includePlan, bool includeApply) =>
            (includePlan, includeApply) switch
            {
                (true, true) => query.ProjectToRunWithPlanAndApply(),
                (true, false) => query.ProjectToRunWithPlan(),
                (false, true) => query.ProjectToRunWithApply(),
                _ => query.ProjectToRun()
            };

        public static IQueryable<Domain.Models.Run> Limit(this IQueryable<Domain.Models.Run> query, int? limit)
        {
            if (limit.HasValue)
            {
                return query.Take(limit.Value);
            }
            else
            {
                return query;
            }
        }
    }
}
