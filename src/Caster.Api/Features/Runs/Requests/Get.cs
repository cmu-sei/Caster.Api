// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;
using System.Text.Json.Serialization;

namespace Caster.Api.Features.Runs
{
    public class Get
    {
        [DataContract(Name="GetRunQuery")]
        public class Query : RunQuery, IRequest<Run>
        {
            /// <summary>
            /// The Id of the Run to retrieve
            /// </summary>
            [JsonIgnore]
            public Guid Id { get; set; }
        }

        public class Handler : IRequestHandler<Query, Run>
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

            public async Task<Run> Handle(Query request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                var run = await _db.Runs
                    .Expand(_mapper.ConfigurationProvider,
                            includePlan: request.IncludePlan,
                            includeApply: request.IncludeApply)
                    .SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken);

                if (run == null)
                    throw new EntityNotFoundException<Run>();

                return run;
            }
        }
    }
}

