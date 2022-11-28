// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Data;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using AutoMapper.QueryableExtensions;
using System.Collections.Generic;
using FluentValidation;
using System.Text.Json.Serialization;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;

namespace Caster.Api.Features.Vlan
{
    public class CreatePartition
    {
        [DataContract(Name = "CreatePartitionCommand")]
        public class Command : IRequest<Partition>
        {
            /// <summary>
            /// The Id of the Pool to create this Partition in
            /// </summary>
            [JsonIgnore]
            public Guid PoolId { get; set; }

            /// <summary>
            /// The Name of this Partition
            /// </summary>
            [DataMember]
            public String Name { get; set; }

            /// <summary>
            /// The number of requested vlans for this Partition
            /// </summary>
            [DataMember]
            public int Vlans { get; set; }
        }

        public class Validator : AbstractValidator<Command>
        {
            public Validator(IValidationService validationService)
            {
                RuleFor(x => x.PoolId).PoolExists(validationService);
                RuleFor(x => x.Vlans)
                    .InclusiveBetween(0, 4095);
            }
        }

        public class Handler : IRequestHandler<Command, Partition>
        {
            private readonly CasterContext _db;
            private readonly IMapper _mapper;
            private readonly IAuthorizationService _authorizationService;
            private readonly ClaimsPrincipal _user;
            private readonly IIdentityResolver _identityResolver;

            public Handler(
                CasterContext db,
                IMapper mapper,
                IAuthorizationService authorizationService,
                IIdentityResolver identityResolver)
            {
                _db = db;
                _mapper = mapper;
                _authorizationService = authorizationService;
                _user = identityResolver.GetClaimsPrincipal();
                _identityResolver = identityResolver;
            }

            public async Task<Partition> Handle(Command command, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                var vlans = await _db.Vlans
                    .Where(x => x.PoolId == command.PoolId && !x.InUse && !x.Reserved)
                    .OrderBy(x => x.VlanId)
                    .Take(command.Vlans)
                    .ToArrayAsync();

                // Verify there are enough available vlans in this pool
                if (vlans.Length < command.Vlans)
                {
                    throw new ConflictException(
                        $"The requested number of VLANs ({command.Vlans}) is greater than the number of available VLANs ({vlans.Length})");
                }

                // Create partition
                var partition = _mapper.Map<Domain.Models.Partition>(command);
                partition.Vlans = vlans;
                _db.Partitions.Add(partition);
                await _db.SaveChangesAsync();

                return _mapper.Map<Partition>(partition);
            }
        }
    }
}
