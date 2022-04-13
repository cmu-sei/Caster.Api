// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using System.Text.Json.Serialization;
using System.Linq;
using Caster.Api.Features.Shared;
using FluentValidation;
using AutoMapper.QueryableExtensions;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;

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
            RuleFor(x => x.DesignId).DesignExists(validationService);
        }
    }

    public class Handler : BaseHandler<Handler>, IRequestHandler<Query, Variable[]>
    {
        public Handler(IDependencyAggregate<Handler> aggregate) : base(aggregate) { }

        public async Task<Variable[]> Handle(Query request, CancellationToken cancellationToken)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                throw new ForbiddenException();

            var variablesQuery = _db.Variables.AsQueryable();

            if (request.DesignId.HasValue)
            {
                variablesQuery = variablesQuery.Where(x => x.DesignId == request.DesignId);
            }

            var variables = await variablesQuery
                .ProjectTo<Variable>(_mapper.ConfigurationProvider)
                .ToArrayAsync(cancellationToken);

            return variables;
        }
    }
}