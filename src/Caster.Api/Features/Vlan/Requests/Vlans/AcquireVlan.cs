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

public class AcquireVlan
{
    [DataContract(Name = "AcquireVlanCommand")]
    public class Command : IRequest<Vlan>
    {
        /// <summary>
        /// The Id of the Project the VLAN should come from
        /// </summary>
        [DataMember]
        public Guid? ProjectId { get; set; }

        /// <summary>
        /// The Id of the partition the VLAN should come from
        /// </summary>
        [DataMember]
        public Guid? PartitionId { get; set; }

        public string Tag { get; set; }
        public int? VlanId { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator(IValidationService validationService)
        {
            RuleFor(x => x.ProjectId.Value).ProjectExists(validationService).When(x => x.ProjectId.HasValue);
            RuleFor(x => x.PartitionId.Value).PartitionExists(validationService).When(x => x.PartitionId.HasValue);
            RuleFor(x => x.VlanId).InclusiveBetween(0, 4095);
        }
    }

    public class Handler : IRequestHandler<Command, Vlan>
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

        public async Task<Vlan> Handle(Command command, CancellationToken cancellationToken)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                throw new ForbiddenException();

            Guid? partitionId = null;

            // find partition
            if (command.PartitionId.HasValue)
            {
                partitionId = await _db.Partitions
                    .Where(x => x.Id == command.PartitionId.Value)
                    .Select(x => x.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }
            else if (command.ProjectId.HasValue)
            {
                partitionId = await _db.Projects
                    .Where(x => x.Id == command.ProjectId.Value)
                    .Select(x => x.PartitionId)
                    .FirstOrDefaultAsync(cancellationToken);
            }
            else
            {
                // Use default partition if one exists
                partitionId = await _db.Partitions
                    .Where(x => x.IsDefault)
                    .Select(x => x.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (partitionId == null)
                throw new EntityNotFoundException<Partition>();

            Domain.Models.Vlan vlan;

            using (var lockResult = await _lockService.GetPartitionLock(partitionId.Value).LockAsync(10000))
            {
                if (!lockResult.AcquiredLock)
                    throw new ConflictException("Could not acquire Partition lock");

                var query = _db.Vlans
                    .Where(x =>
                        x.PartitionId == partitionId.Value &&
                        !x.InUse &&
                        !x.Reserved);

                if (command.VlanId.HasValue || !string.IsNullOrEmpty(command.Tag))
                {
                    if (command.VlanId.HasValue)
                    {
                        query = query.Where(x => x.VlanId == command.VlanId);
                    }

                    if (!string.IsNullOrEmpty(command.Tag))
                    {
                        query = query.Where(x => x.Tag == command.Tag);
                    }

                    vlan = await query.FirstOrDefaultAsync(cancellationToken);
                }
                else
                {
                    vlan = await query
                        .OrderBy(x => x.VlanId)
                        .Take(1)
                        .FirstOrDefaultAsync(cancellationToken);
                }

                if (vlan == null)
                    throw new ConflictException("No VLANs available");

                vlan.InUse = true;
                await _db.SaveChangesAsync(cancellationToken);
            }

            return _mapper.Map<Vlan>(vlan);
        }
    }
}