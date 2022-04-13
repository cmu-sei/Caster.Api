// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Shared;
using FluentValidation;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Variables;

public class Edit
{
    [DataContract(Name = "EditVariableCommand")]
    public record Command : IRequest<Variable>
    {
        [JsonIgnore]
        public Guid Id { get; set; }

        public string Name { get; init; }
        public VariableType Type { get; init; }
        public string DefaultValue { get; init; }
    }

    public class Handler : BaseHandler<Handler>, IRequestHandler<Command, Variable>
    {
        public Handler(IDependencyAggregate<Handler> aggregate) : base(aggregate) { }

        public async Task<Variable> Handle(Command request, CancellationToken cancellationToken)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                throw new ForbiddenException();

            var variable = await _db.Variables
                .Where(x => x.Id == request.Id)
                .SingleOrDefaultAsync(cancellationToken);

            if (variable == null)
                throw new EntityNotFoundException<Variable>();

            _mapper.Map(request, variable);

            await _db.SaveChangesAsync(cancellationToken);

            return _mapper.Map<Variable>(variable);
        }
    }
}
