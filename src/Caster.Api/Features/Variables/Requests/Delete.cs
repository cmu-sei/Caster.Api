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
using System.Text.Json.Serialization;
using Caster.Api.Domain.Models;
using Caster.Api.Data;

namespace Caster.Api.Features.Variables;

public class Delete
{
    [DataContract(Name = "DeleteVariableCommand")]
    public record Command : IRequest
    {
        [JsonIgnore]
        public Guid Id { get; set; }
    }

    public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext) : BaseHandler<Command>
    {
        public override async Task Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<Domain.Models.Variable>(request.Id, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

        public override async Task HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var variable = await dbContext.Variables.FindAsync([request.Id], cancellationToken);

            if (variable == null)
                throw new EntityNotFoundException<Variable>();

            dbContext.Variables.Remove(variable);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
