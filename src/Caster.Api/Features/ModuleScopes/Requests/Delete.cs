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

namespace Caster.Api.Features.ModuleScopes;

public class Delete
{
    [DataContract(Name = "DeleteModuleScopeCommand")]
    public class Command : IRequest
    {
        public Guid Id { get; set; }
    }

    /// <summary>
    /// Deletes the scope and its assignments. A project left with no scope may use every Module again,
    /// so the UI confirms, listing the projects and groups, before calling this.
    /// </summary>
    public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext) : BaseHandler<Command>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageModules], cancellationToken);

        public override async Task HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var scope = await dbContext.ModuleScopes.SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
                ?? throw new EntityNotFoundException<Domain.Models.ModuleScope>();

            dbContext.ModuleScopes.Remove(scope);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
