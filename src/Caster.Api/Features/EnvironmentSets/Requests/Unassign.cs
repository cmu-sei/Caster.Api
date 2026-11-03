// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Shared;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.EnvironmentSets;

public class Unassign
{
    [DataContract(Name = "UnassignEnvironmentSetCommand")]
    public class Command : IRequest
    {
        public Guid ProjectId { get; set; }
        public Guid EnvironmentSetId { get; set; }
    }

    public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext) : BaseHandler<Command>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageEnvironmentSets], cancellationToken);

        public override async Task HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var assignment = await dbContext.ProjectEnvironmentSets
                .SingleOrDefaultAsync(x => x.ProjectId == request.ProjectId && x.EnvironmentSetId == request.EnvironmentSetId, cancellationToken)
                ?? throw new EntityNotFoundException<ProjectEnvironmentSet>("Environment Set is not assigned to this project");

            dbContext.ProjectEnvironmentSets.Remove(assignment);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
