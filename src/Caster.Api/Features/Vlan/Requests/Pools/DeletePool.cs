// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
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
using System.Linq;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using FluentValidation;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;

namespace Caster.Api.Features.Vlan
{
    public class DeletePool
    {
        [DataContract(Name = "DeletePoolCommand")]
        public class Command : IRequest
        {
            [JsonIgnore]
            public Guid Id { get; set; }

            /// <summary>
            /// Delete this Pool even if it has VLANs in use
            /// </summary>
            [DataMember]
            public bool Force { get; set; }
        }

        public class Validator : AbstractValidator<Command>
        {
            public Validator(IValidationService validationService)
            {
                RuleFor(x => x.Id).PoolExists(validationService);
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

            public async Task Handle(Command request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                var pool = await _db.Pools
                    .Where(x => x.Id == request.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                if (!request.Force)
                {
                    var inUse = await _db.Vlans
                    .Where(x => x.PoolId == pool.Id && x.InUse)
                    .AnyAsync(cancellationToken);

                    if (inUse)
                    {
                        throw new ConflictException("Cannot delete a Pool with VLANs in use. Use the Force option to override.");
                    }
                }

                _db.Pools.Remove(pool);
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
