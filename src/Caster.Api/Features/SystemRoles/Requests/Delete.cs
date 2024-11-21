// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using System.Runtime.Serialization;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.SystemRoles
{
    public class Delete
    {
        [DataContract(Name = "DeleteSystemRoleCommand")]
        public class Command : IRequest
        {
            public Guid Id { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Command>
        {
            public override async Task Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ManageRoles], cancellationToken);

            public override async Task HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var systemRole = await dbContext.SystemRoles.FindAsync([request.Id], cancellationToken);

                if (systemRole == null)
                    throw new EntityNotFoundException<SystemRole>();

                if (systemRole.Immutable)
                    throw new ConflictException("Immutable Role cannot be deleted.");

                dbContext.SystemRoles.Remove(systemRole);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
    }
}

