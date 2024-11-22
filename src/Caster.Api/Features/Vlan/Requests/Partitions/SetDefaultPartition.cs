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
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Vlan
{
    public class SetDefaultPartition
    {
        [DataContract(Name = "SetDefaultPartitionCommand")]
        public class Command : IRequest
        {
            [JsonIgnore]
            public Guid? Id { get; set; }
        }

        public class Validator : AbstractValidator<Command>
        {
            public Validator(IValidationService validationService)
            {
                RuleFor(x => x.Id.Value).PartitionExists(validationService).When(x => x.Id.HasValue);
            }
        }

        public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext) : BaseHandler<Command>
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ManageVLANs], cancellationToken);

            public override async Task HandleRequest(Command command, CancellationToken cancellationToken)
            {
                // Ensure that only one partition/pool can be Default
                using var transaction = dbContext.Database.BeginTransaction(System.Data.IsolationLevel.Serializable);

                var currentDefaultPool = await dbContext.Pools
                    .Where(x => x.IsDefault)
                    .FirstOrDefaultAsync(cancellationToken);

                var currentDefaultPartition = await dbContext.Partitions
                    .Where(x => x.IsDefault)
                    .FirstOrDefaultAsync(cancellationToken);

                if (currentDefaultPool != null)
                    currentDefaultPool.IsDefault = false;

                if (currentDefaultPartition != null)
                    currentDefaultPartition.IsDefault = false;

                if (command.Id.HasValue)
                {
                    var partition = await dbContext.Partitions
                        .Where(x => x.Id == command.Id)
                        .Include(x => x.Pool)
                        .FirstOrDefaultAsync(cancellationToken);

                    partition.IsDefault = true;
                    partition.Pool.IsDefault = true;
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
        }
    }
}
