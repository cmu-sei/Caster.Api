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
using AutoMapper.QueryableExtensions;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Domain.Models;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;

namespace Caster.Api.Features.Plans
{
    public class GetByRun
    {
        [DataContract(Name="GetPlanByRunQuery")]
        public class Query : IRequest<Plan>
        {
            /// <summary>
            /// The Id of the Run whose Plan to retrieve
            /// </summary>
            /// <value></value>
            [DataMember]
            public Guid RunId { get; set; }
        }

        public class Handler : IRequestHandler<Query, Plan>
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

            public async Task<Plan> Handle(Query request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                await ValidateRun(request.RunId);

                var plan = await _db.Plans
                    .ProjectTo<Plan>(_mapper.ConfigurationProvider)
                    .SingleOrDefaultAsync(x => x.RunId == request.RunId, cancellationToken);

                if (plan == null)
                    throw new EntityNotFoundException<Plan>();

                return plan;
            }

            private async Task ValidateRun(Guid runId)
            {
                var run = await _db.Runs.FindAsync(runId);

                if (run == null)
                    throw new EntityNotFoundException<Run>();
            }
        }
    }
}

