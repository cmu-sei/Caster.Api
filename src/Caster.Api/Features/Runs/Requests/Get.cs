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
using Caster.Api.Infrastructure.Authorization;
using System.Text.Json.Serialization;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Runs
{
    public class Get
    {
        [DataContract(Name = "GetRunQuery")]
        public class Query : RunQuery, IRequest<Run>
        {
            /// <summary>
            /// The Id of the Run to retrieve
            /// </summary>
            [JsonIgnore]
            public Guid Id { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, Run>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Domain.Models.Run>(request.Id, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], cancellationToken);

            public override async Task<Run> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                var run = await dbContext.Runs
                    .Expand(mapper.ConfigurationProvider,
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

