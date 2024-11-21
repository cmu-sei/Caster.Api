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

namespace Caster.Api.Features.Modules
{
    public class Delete
    {
        [DataContract(Name = "DeleteModuleCommand")]
        public class Command : IRequest
        {
            public Guid Id { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext) : BaseHandler<Command>
        {
            public override async Task Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermissions.ManageModules], cancellationToken);

            public override async Task HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var module = await dbContext.Modules.FindAsync([request.Id], cancellationToken);

                if (module == null)
                    throw new EntityNotFoundException<Module>();

                dbContext.Modules.Remove(module);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
    }
}

