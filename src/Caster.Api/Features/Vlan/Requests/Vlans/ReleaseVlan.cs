// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Domain.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using FluentValidation;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Vlan;

public class ReleaseVlan
{
    [DataContract(Name = "ReleaseVlan")]
    public class Command : IRequest<Vlan>
    {
        /// <summary>
        /// The Id of the vlan to be released
        /// </summary>
        [JsonIgnore]
        public Guid Id { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator(IValidationService validationService)
        {
            RuleFor(x => x.Id).VlanExists(validationService);
        }
    }

    public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext, ILockService lockService) : BaseHandler<Command, Vlan>
    {
        public override async Task Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermissions.EditVLANs], cancellationToken);

        public override async Task<Vlan> HandleRequest(Command command, CancellationToken cancellationToken)
        {
            var vlan = await dbContext.Vlans
                .Where(x => x.Id == command.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (!vlan.PartitionId.HasValue)
                throw new ConflictException("Only VLANs assigned to a Partition can be acquired or released.");

            using (var lockResult = await lockService.GetPartitionLock(vlan.PartitionId.Value).LockAsync(10000))
            {
                if (!lockResult.AcquiredLock)
                    throw new ConflictException("Could not acquire Partition lock");

                vlan.InUse = false;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return mapper.Map<Vlan>(vlan);
        }
    }
}
