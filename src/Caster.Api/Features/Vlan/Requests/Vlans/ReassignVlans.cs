// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Data;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Domain.Services;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using FluentValidation;
using System.Collections.Generic;
using Caster.Api.Utilities.Synchronization;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;

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

    public class Handler : IRequestHandler<Command, Vlan[]>
    {
        private readonly CasterContext _db;
        private readonly IMapper _mapper;
        private readonly IAuthorizationService _authorizationService;
        private readonly ClaimsPrincipal _user;
        private readonly ILockService _lockService;

        public Handler(
            CasterContext db,
            IMapper mapper,
            IAuthorizationService authorizationService,
            IIdentityResolver identityResolver,
            ILockService lockService)
        {
            _db = db;
            _mapper = mapper;
            _authorizationService = authorizationService;
            _user = identityResolver.GetClaimsPrincipal();
            _lockService = lockService;
        }

        public async Task<Vlan[]> Handle(Command command, CancellationToken cancellationToken)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                throw new ForbiddenException();

            Domain.Models.Vlan[] vlans;

            // Order partitions by id so we always obtain locks in the same order to avoid deadlocking
            var locks = new List<AsyncLock>();
            var lockIds = new List<Guid>() { command.FromPartitionId, command.ToPartitionId }.OrderBy(x => x).ToList();

            foreach (var lockId in lockIds)
            {
                locks.Add(_lockService.GetPartitionLock(lockId));
            }

            // Lock poolId as proxy for unassigned partition of that pool
            using (var firstLockResult = await locks[0].LockAsync(10000))
            using (var secondLockResult = await locks[1].LockAsync(10000))
            {
                if (!firstLockResult.AcquiredLock || !secondLockResult.AcquiredLock)
                    throw new ConflictException("Could not acquire Partition lock");

                vlans = await _db.Vlans
                    .Where(x => command.VlanIds.Contains(x.Id))
                    .ToArrayAsync(cancellationToken);

                if (vlans.Select(x => x.PartitionId).Where(y => y != command.FromPartitionId).Any())
                    throw new ConflictException("All VLANs must be from the same starting Partition");

                foreach (var vlan in vlans)
                {
                    vlan.PartitionId = command.ToPartitionId;
                }

                await _db.SaveChangesAsync(cancellationToken);
            }

            return _mapper.Map<Vlan[]>(vlans);
        }
    }
}