// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Authorization;
using System.Linq;
using Caster.Api.Features.Shared;
using FluentValidation;
using AutoMapper.QueryableExtensions;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;
using AutoMapper;
using Caster.Api.Data;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Variables;

public class GetAll
{
    [DataContract(Name = "GetAllVariablesQuery")]
    public record Query : IRequest<Variable[]>
    {
        public Guid? DesignId { get; init; }
    }

    public class Validator : AbstractValidator<Query>
    {
        public Validator(IValidationService validationService)
        {
            RuleFor(x => x.DesignId.Value).DesignExists(validationService).When(x => x.DesignId.HasValue);
        }
    }

    public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, Variable[]>
    {
        public override async Task Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<Design>(request.DesignId, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], cancellationToken);

        public override async Task<Variable[]> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var variablesQuery = dbContext.Variables.AsQueryable();

            if (request.DesignId.HasValue)
            {
                variablesQuery = variablesQuery.Where(x => x.DesignId == request.DesignId);
            }

            var variables = await variablesQuery
                .ProjectTo<Variable>(mapper.ConfigurationProvider)
                .ToArrayAsync(cancellationToken);

            return variables;
        }
    }
}