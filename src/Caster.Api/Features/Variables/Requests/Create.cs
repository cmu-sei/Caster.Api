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
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Variables;

public class Create
{
    [DataContract(Name = "CreateVariableCommand")]
    public record Command : IRequest<Variable>
    {
        public Guid DesignId { get; set; }
        public string Name { get; set; }
        public VariableType Type { get; set; }
        public string DefaultValue { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator(IValidationService validationService)
        {
            RuleFor(x => x.DesignId).DesignExists(validationService);
        }
    }

    public class Handler : BaseHandler<Handler>, IRequestHandler<Command, Variable>
    {
        public Handler(IDependencyAggregate<Handler> aggregate) : base(aggregate) { }

        public async Task<Variable> Handle(Command request, CancellationToken cancellationToken)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                throw new ForbiddenException();

            var variable = _mapper.Map<Domain.Models.Variable>(request);

            _db.Variables.Add(variable);
            await _db.SaveChangesAsync(cancellationToken);

            return _mapper.Map<Variable>(variable);
        }
    }
}
