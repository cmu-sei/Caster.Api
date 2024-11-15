// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Hosts
{
    public class Delete
    {
        [DataContract(Name = "DeleteHostCommand")]
        public class Command : IRequest
        {
            public Guid Id { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext) : BaseHandler<Command>
        {
            public override async Task Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermissions.EditHosts], cancellationToken);

            public override async Task HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var entry = await dbContext.Hosts.FindAsync([request.Id], cancellationToken);

                if (entry == null)
                    throw new EntityNotFoundException<Host>();

                dbContext.Hosts.Remove(entry);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
    }
}

