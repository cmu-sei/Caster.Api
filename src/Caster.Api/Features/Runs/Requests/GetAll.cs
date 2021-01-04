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
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Domain.Models;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;

namespace Caster.Api.Features.Runs
{
    public class GetAll
    {
        [DataContract(Name="GetAllRunsQuery")]
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

        public class Handler : IRequestHandler<Query, Run[]>
        {
            private readonly CasterContext _db;
            private readonly IMapper _mapper;
            private readonly IAuthorizationService _authorizationService;
            private readonly ClaimsPrincipal _user;

            public Handler(
                CasterContext db,
                IMapper mapper,
                IAuthorizationService authorizationService,
                IIdentityResolver identityResolver)
            {
                _db = db;
                _mapper = mapper;
                _authorizationService = authorizationService;
                _user = identityResolver.GetClaimsPrincipal();
            }

            public async Task<Run[]> Handle(Query request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded)
                    throw new ForbiddenException();

                var query = _db.Runs
                    .OrderByDescending(r => r.CreatedAt)
                    .Limit(request.Limit)
                    .Expand(_mapper.ConfigurationProvider,
                            includePlan: request.IncludePlan,
                            includeApply: request.IncludeApply);

                if (request.ActiveOnly)
                {
                    query = query.Where(x => RunHelpers.GetActiveStatuses().Contains(x.Status));
                }

                return await query.ToArrayAsync();
            }
        }
    }
}
