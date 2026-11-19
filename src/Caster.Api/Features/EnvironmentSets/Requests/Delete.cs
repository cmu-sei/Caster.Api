// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
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

public class Delete
{
    [DataContract(Name = "DeleteEnvironmentSetCommand")]
    public class Command : IRequest
    {
        public Guid Id { get; set; }
    }

    /// <summary>
    /// Deletes the set and removes it from every project it is assigned to; their next runs
    /// do not get its variables. The UI confirms, listing those projects, before calling this.
    /// </summary>
    public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext) : BaseHandler<Command>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageEnvironmentSets], cancellationToken);

        public override async Task HandleRequest(Command request, CancellationToken cancellationToken)
        {
            // Loaded with its entries and assignments so their removal raises entity events,
            // which tell each affected project's members.
            var set = await dbContext.EnvironmentSets
                .Include(x => x.Entries)
                .Include(x => x.Projects)
                .AsSplitQuery()
                .SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
                ?? throw new EntityNotFoundException<EnvironmentSet>();

            dbContext.EnvironmentSets.Remove(set);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
