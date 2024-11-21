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
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using FluentValidation;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Vlan;

public class RemoveVlansFromPartition
{
    [DataContract(Name = "RemoveVlansFromPartitionCommand")]
    public class Command : IRequest<Vlan[]>
    {
        /// <summary>
        /// The Id of the partition the VLANs should be removed from
        /// </summary>
        [JsonIgnore]
        public Guid PartitionId { get; set; }

        /// <summary>
        /// The number of VLANs to remove. Ignored if VlanIds are specified
        /// </summary>
        public int? Vlans { get; set; }

        /// <summary>
        /// List of specific Vlans to remove.
        /// </summary>
        public Guid[] VlanIds { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator(IValidationService validationService)
        {
            RuleFor(x => x.PartitionId).PartitionExists(validationService);
        }
    }

    public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext, ILockService lockService) : BaseHandler<Command, Vlan[]>
    {
        public override async Task Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermissions.ManageVLANs], cancellationToken);

        public override async Task<Vlan[]> HandleRequest(Command command, CancellationToken cancellationToken)
        {
            Domain.Models.Vlan[] vlans;

            using (var lockResult = await lockService.GetPartitionLock(command.PartitionId).LockAsync(10000))
            {
                if (!lockResult.AcquiredLock)
                    throw new ConflictException("Could not acquire Partition lock");

                var query = dbContext.Vlans
                        .Where(x =>
                            x.PartitionId == command.PartitionId &&
                            !x.InUse);

                if (command.VlanIds != null && command.VlanIds.Any())
                {
                    vlans = await query
                        .Where(x => command.VlanIds.Contains(x.Id))
                        .ToArrayAsync(cancellationToken);
                }
                else if (command.Vlans.HasValue)
                {
                    vlans = await query
                        .OrderBy(x => x.VlanId)
                        .Take(command.Vlans.Value)
                        .ToArrayAsync(cancellationToken);

                    if (vlans.Length < command.Vlans)
                        throw new ConflictException($"Not enough VLANs available. Requested {command.Vlans} but only {vlans.Length} VLANs in this partition are not in use");
                }
                else
                {
                    throw new ConflictException("Either VlanIds or Vlans must be specified.");
                }

                foreach (var vlan in vlans)
                {
                    vlan.PartitionId = null;
                }

                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return mapper.Map<Vlan[]>(vlans);
        }
    }
}