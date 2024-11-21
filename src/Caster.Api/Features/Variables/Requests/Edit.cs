// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Shared;
using FluentValidation;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using Caster.Api.Domain.Models;
using AutoMapper;
using Caster.Api.Data;

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

    public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Command, Variable>
    {
        public override async Task Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<Domain.Models.Variable>(request.Id, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

        public override async Task<Variable> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var variable = await dbContext.Variables
                .Where(x => x.Id == request.Id)
                .SingleOrDefaultAsync(cancellationToken);

            if (variable == null)
                throw new EntityNotFoundException<Variable>();

            mapper.Map(request, variable);
            await dbContext.SaveChangesAsync(cancellationToken);

            return mapper.Map<Variable>(variable);
        }
    }
}
