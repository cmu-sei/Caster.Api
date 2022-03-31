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
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using FluentValidation;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;

namespace Caster.Api.Features.Vlan;

public class ReleaseVlan
{
    [DataContract(Name = "ReleaseVlan")]
    public class Command : IRequest<Vlan>
    {
        /// <summary>
        /// The Id of the vlan to be returned
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

            var vlan = await _db.Vlans
                .Where(x => x.Id == command.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (!vlan.PartitionId.HasValue)
                throw new ConflictException("Only VLANs assigned to a Partition can be acquired or released.");

            using (var lockResult = await _lockService.GetPartitionLock(vlan.PartitionId.Value).LockAsync(10000))
            {
                if (!lockResult.AcquiredLock)
                    throw new ConflictException("Could not acquire Partition lock");

                vlan.InUse = false;
                await _db.SaveChangesAsync(cancellationToken);
            }

            return _mapper.Map<Vlan>(vlan);
        }
    }
}
