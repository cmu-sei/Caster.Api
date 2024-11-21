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
using Microsoft.EntityFrameworkCore;
using FluentValidation;
using System.Text.Json.Serialization;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

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

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Command, Partition>
        {
            public override async Task Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermissions.ManageVLANs], cancellationToken);

            public override async Task<Partition> HandleRequest(Command command, CancellationToken cancellationToken)
            {
                var vlans = await dbContext.Vlans
                    .Where(x => x.PoolId == command.PoolId && !x.InUse && !x.Reserved)
                    .OrderBy(x => x.VlanId)
                    .Take(command.Vlans)
                    .ToArrayAsync(cancellationToken);

                // Verify there are enough available vlans in this pool
                if (vlans.Length < command.Vlans)
                {
                    throw new ConflictException(
                        $"The requested number of VLANs ({command.Vlans}) is greater than the number of available VLANs ({vlans.Length})");
                }

                // Create partition
                var partition = mapper.Map<Domain.Models.Partition>(command);
                partition.Vlans = vlans;
                dbContext.Partitions.Add(partition);
                await dbContext.SaveChangesAsync(cancellationToken);

                return mapper.Map<Partition>(partition);
            }
        }
    }
}
