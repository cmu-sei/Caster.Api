// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Runtime.Serialization;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Shared;

namespace Caster.Api.Features.Runs
{
    public class GetAll
    {
        [DataContract(Name = "GetAllRunsQuery")]
        public class Query : RunQuery, IRequest<Run[]>
        {
            /// <summary>
            /// If true, only return Active Runs.
            /// </summary>
            public bool ActiveOnly { get; set; }

            /// <summary>
            /// Limit the number of results returned to this amount if present
            /// </summary>
            public int? Limit { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, Run[]>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ViewProjects, SystemPermission.ViewWorkspaces], cancellationToken);

            public override async Task<Run[]> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                var query = dbContext.Runs
                    .OrderByDescending(r => r.CreatedAt)
                    .Limit(request.Limit)
                    .Expand(mapper.ConfigurationProvider,
                            includePlan: request.IncludePlan,
                            includeApply: request.IncludeApply);

                if (request.ActiveOnly)
                {
                    query = query.Where(x => RunHelpers.GetActiveStatuses().Contains(x.Status));
                }

                return await query.ToArrayAsync(cancellationToken);
            }
        }
    }
}
