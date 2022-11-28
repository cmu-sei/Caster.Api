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
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;

namespace Caster.Api.Features.Vlan;

public class AddVlansToPartition
{
    [DataContract(Name = "AddVlansToPartitionCommand")]
    public class Command : IRequest<Vlan[]>
    {
        /// <summary>
        /// The Id of the partition the VLANs should be added to
        /// </summary>
        [JsonIgnore]
        public Guid PartitionId { get; set; }

        /// <summary>
        /// The number of VLANs to add to the partition
        /// </summary>
        public int Vlans { get; set; }

        /// <summary>
        /// List of specific Vlans to add.
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

            var partition = await _db.Partitions
                .Where(x => x.Id == command.PartitionId)
                .FirstOrDefaultAsync(cancellationToken);

            Domain.Models.Vlan[] vlans;

            // Lock poolId as proxy for unassigned partition of that pool
            using (var lockResult = await _lockService.GetPartitionLock(partition.PoolId).LockAsync(10000))
            {
                if (!lockResult.AcquiredLock)
                    throw new ConflictException("Could not acquire Partition lock");

                var query = _db.Vlans
                        .Where(x =>
                            x.PoolId == partition.PoolId &&
                            !x.PartitionId.HasValue &&
                            !x.InUse &&
                            !x.Reserved);

                if (command.VlanIds != null && command.VlanIds.Any())
                {
                    vlans = await query
                        .Where(x => command.VlanIds.Contains(x.Id))
                        .ToArrayAsync(cancellationToken);
                }
                else
                {
                    vlans = await query
                        .OrderBy(x => x.VlanId)
                        .Take(command.Vlans)
                        .ToArrayAsync(cancellationToken);

                    if (vlans.Length < command.Vlans)
                        throw new ConflictException($"Not enough VLANs available. Requested {command.Vlans} but only {vlans.Length} are unassigned");
                }

                foreach (var vlan in vlans)
                {
                    vlan.PartitionId = command.PartitionId;
                }

                await _db.SaveChangesAsync(cancellationToken);
            }

            return _mapper.Map<Vlan[]>(vlans);
        }
    }
}