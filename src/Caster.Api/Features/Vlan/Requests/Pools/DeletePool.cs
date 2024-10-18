// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using FluentValidation;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Vlan
{
    public class DeletePool
    {
        [DataContract(Name = "DeletePoolCommand")]
        public class Command : IRequest
        {
            [JsonIgnore]
            public Guid Id { get; set; }

            /// <summary>
            /// Delete this Pool even if it has VLANs in use
            /// </summary>
            [DataMember]
            public bool Force { get; set; }
        }

        public class Validator : AbstractValidator<Command>
        {
            public Validator(IValidationService validationService)
            {
                RuleFor(x => x.Id).PoolExists(validationService);
            }
        }

        public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext) : BaseHandler<Command>
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ManageVLANs], cancellationToken);

            public override async Task HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var pool = await dbContext.Pools
                    .Where(x => x.Id == request.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                if (!request.Force)
                {
                    var inUse = await dbContext.Vlans
                    .Where(x => x.PoolId == pool.Id && x.InUse)
                    .AnyAsync(cancellationToken);

                    if (inUse)
                    {
                        throw new ConflictException("Cannot delete a Pool with VLANs in use. Use the Force option to override.");
                    }
                }

                dbContext.Pools.Remove(pool);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
