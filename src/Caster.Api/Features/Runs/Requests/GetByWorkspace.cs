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
using System.Text.Json.Serialization;
using Caster.Api.Features.Shared;
using FluentValidation;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;

namespace Caster.Api.Features.Runs
{
    public class GetByWorkspace
    {
        [DataContract(Name = "GetRunsByWorkspaceQuery")]
        public class Query : RunQuery, IRequest<Run[]>
        {
            /// <summary>
            /// The Id of the Workspace whose Runs to retrieve
            /// </summary>
            [JsonIgnore]
            public Guid WorkspaceId { get; set; }

            /// <summary>
            /// Limit the number of results returned to this amount if present
            /// </summary>
            public int? Limit { get; set; }
        }

        public class RequestValidator : AbstractValidator<Query>
        {
            public RequestValidator(IValidationService validationService)
            {
                RuleFor(x => x.WorkspaceId).WorkspaceExists(validationService);
            }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, Run[]>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Domain.Models.Workspace>(request.WorkspaceId, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], cancellationToken);

            public override async Task<Run[]> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                return await dbContext.Runs
                    .Where(x => x.WorkspaceId == request.WorkspaceId)
                    .OrderByDescending(r => r.CreatedAt)
                    .Limit(request.Limit)
                    .Expand(mapper.ConfigurationProvider,
                            includePlan: request.IncludePlan,
                            includeApply: request.IncludeApply)
                    .ToArrayAsync(cancellationToken);
            }
        }
    }
}

