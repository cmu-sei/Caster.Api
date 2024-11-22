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
using Caster.Api.Features.Shared;
using FluentValidation;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;

namespace Caster.Api.Features.Plans
{
    public class GetByRun
    {
        [DataContract(Name = "GetPlanByRunQuery")]
        public class Query : IRequest<Plan>
        {
            /// <summary>
            /// The Id of the Run whose Plan to retrieve
            /// </summary>
            /// <value></value>
            [DataMember]
            public Guid RunId { get; set; }
        }

        public class RequestValidator : AbstractValidator<Query>
        {
            public RequestValidator(IValidationService validationService)
            {
                RuleFor(x => x.RunId).RunExists(validationService);
            }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, Plan>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Run>(request.RunId, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], cancellationToken);

            public override async Task<Plan> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                var plan = await dbContext.Plans
                    .ProjectTo<Plan>(mapper.ConfigurationProvider)
                    .SingleOrDefaultAsync(x => x.RunId == request.RunId, cancellationToken);

                if (plan == null)
                    throw new EntityNotFoundException<Plan>();

                return plan;
            }
        }
    }
}

