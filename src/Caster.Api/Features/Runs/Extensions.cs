// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.


using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using AutoMapper;
using AutoMapper.QueryableExtensions;

namespace Caster.Api.Features.Runs
{
    public static class RunsExtensions
    {
        public static IQueryable<Run> Expand(this IQueryable<Domain.Models.Run> query, IConfigurationProvider configuration, bool includePlan, bool includeApply)
        {
            var includeList = new List<Expression<System.Func<Run, object>>>();

            if (includePlan) includeList.Add(dest => dest.Plan);
            if (includeApply) includeList.Add(dest => dest.Apply);

            return query.ProjectTo<Run>(configuration, includeList.ToArray());
        }

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
