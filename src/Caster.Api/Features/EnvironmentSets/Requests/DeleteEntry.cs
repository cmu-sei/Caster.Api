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

public class DeleteEntry
{
    [DataContract(Name = "DeleteEnvironmentSetEntryCommand")]
    public class Command : IRequest<EnvironmentSet>
    {
        public Guid EnvironmentSetId { get; set; }
        public Guid Id { get; set; }
    }

    public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext, IEnvironmentSetViews views) : BaseHandler<Command, EnvironmentSet>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageEnvironmentSets], cancellationToken);

        public override async Task<EnvironmentSet> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var entry = await dbContext.EnvironmentSetEntries
                .SingleOrDefaultAsync(x => x.Id == request.Id && x.EnvironmentSetId == request.EnvironmentSetId, cancellationToken)
                ?? throw new EntityNotFoundException<EnvironmentSetEntry>();

            dbContext.EnvironmentSetEntries.Remove(entry);
            await dbContext.SaveChangesAsync(cancellationToken);

            return await views.GetSet(request.EnvironmentSetId, cancellationToken);
        }
    }
}
