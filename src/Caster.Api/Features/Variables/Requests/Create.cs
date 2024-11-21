// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Shared;
using FluentValidation;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Domain.Models;
using AutoMapper;
using Caster.Api.Data;

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

    public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Command, Variable>
    {
        public override async Task Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<Design>(request.DesignId, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

        public override async Task<Variable> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var variable = mapper.Map<Domain.Models.Variable>(request);

            dbContext.Variables.Add(variable);
            await dbContext.SaveChangesAsync(cancellationToken);

            return mapper.Map<Variable>(variable);
        }
    }
}
