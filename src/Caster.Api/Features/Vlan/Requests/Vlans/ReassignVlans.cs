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
using FluentValidation;
using System.Collections.Generic;
using Caster.Api.Utilities.Synchronization;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Vlan;

public class ReassignVlans
{
    [DataContract(Name = "ReassignVlansCommand")]
    public class Command : IRequest<Vlan[]>
    {
        /// <summary>
        /// The Id of the partition the VLANs are being reassigned from
        /// </summary>
        public Guid FromPartitionId { get; set; }

        /// <summary>
        /// The Id of the partition the VLANs should be reassigned to
        /// </summary>
        public Guid ToPartitionId { get; set; }

        /// <summary>
        /// The VLANs to reassign
        /// </summary>
        public Guid[] VlanIds { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator(IValidationService validationService)
        {
            RuleFor(x => x.VlanIds)
                .NotEmpty();
            RuleFor(x => x.ToPartitionId).PartitionExists(validationService);
            RuleFor(x => x.FromPartitionId).PartitionExists(validationService);
            RuleFor(x => x.FromPartitionId)
                .Must((command, fromPartitionId) => fromPartitionId != command.ToPartitionId)
                .WithMessage("Cannot reassign VLANs to their existing Partition");
        }
    }

    public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext, ILockService lockService) : BaseHandler<Command, Vlan[]>
    {
        public override async Task Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermissions.EditVLANs], cancellationToken);

        public override async Task<Vlan[]> HandleRequest(Command command, CancellationToken cancellationToken)
        {
            Domain.Models.Vlan[] vlans;

            // Order partitions by id so we always obtain locks in the same order to avoid deadlocking
            var locks = new List<AsyncLock>();
            var lockIds = new List<Guid>() { command.FromPartitionId, command.ToPartitionId }.OrderBy(x => x).ToList();

            foreach (var lockId in lockIds)
            {
                locks.Add(lockService.GetPartitionLock(lockId));
            }

            using (var firstLockResult = await locks[0].LockAsync(10000))
            using (var secondLockResult = await locks[1].LockAsync(10000))
            {
                if (!firstLockResult.AcquiredLock || !secondLockResult.AcquiredLock)
                    throw new ConflictException("Could not acquire Partition lock");

                vlans = await dbContext.Vlans
                    .Where(x => command.VlanIds.Contains(x.Id))
                    .ToArrayAsync(cancellationToken);

                if (vlans.Select(x => x.PartitionId).Where(y => y != command.FromPartitionId).Any())
                    throw new ConflictException("All VLANs must be from the same starting Partition");

                foreach (var vlan in vlans)
                {
                    vlan.PartitionId = command.ToPartitionId;
                }

                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return mapper.Map<Vlan[]>(vlans);
        }
    }
}