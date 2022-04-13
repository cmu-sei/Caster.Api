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

namespace Caster.Api.Features.Variables;

public class Get
{
    [DataContract(Name = "GetVariableQuery")]
    public record Query : IRequest<Variable>
    {
        [JsonIgnore]
        public Guid Id { get; set; }
    }

    public class Handler : BaseHandler<Handler>, IRequestHandler<Query, Variable>
    {
        public Handler(IDependencyAggregate<Handler> aggregate) : base(aggregate) { }

        public async Task<Variable> Handle(Query request, CancellationToken cancellationToken)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                throw new ForbiddenException();

            var variable = await _db.Variables
                .Where(x => x.Id == request.Id)
                .ProjectTo<Variable>(_mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(cancellationToken);

            if (variable == null)
                throw new EntityNotFoundException<Variable>();

            return variable;
        }
    }
}