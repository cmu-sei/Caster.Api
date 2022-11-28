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
    public class SetDefaultPartition
    {
        [DataContract(Name = "SetDefaultPartitionCommand")]
        public class Command : IRequest<Unit>
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

        public class Handler : IRequestHandler<Command>
        {
            private readonly CasterContext _db;
            private readonly IMapper _mapper;
            private readonly IAuthorizationService _authorizationService;
            private readonly ClaimsPrincipal _user;

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
            }

            public async Task<Unit> Handle(Command command, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                // Ensure that only one partition/pool can be Default
                using var transaction = _db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable);

                var currentDefaultPool = await _db.Pools
                    .Where(x => x.IsDefault)
                    .FirstOrDefaultAsync(cancellationToken);

                var currentDefaultPartition = await _db.Partitions
                    .Where(x => x.IsDefault)
                    .FirstOrDefaultAsync(cancellationToken);

                if (currentDefaultPool != null)
                    currentDefaultPool.IsDefault = false;

                if (currentDefaultPartition != null)
                    currentDefaultPartition.IsDefault = false;

                if (command.Id.HasValue)
                {
                    var partition = await _db.Partitions
                        .Where(x => x.Id == command.Id)
                        .Include(x => x.Pool)
                        .FirstOrDefaultAsync(cancellationToken);

                    partition.IsDefault = true;
                    partition.Pool.IsDefault = true;
                }

                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return Unit.Value;
            }
        }
    }
}
