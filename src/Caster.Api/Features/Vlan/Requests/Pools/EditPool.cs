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
using Microsoft.EntityFrameworkCore;
using FluentValidation;
using System.Text.Json.Serialization;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;

namespace Caster.Api.Features.Vlan
{
    public class EditPool
    {
        [DataContract(Name = "EditPoolCommand")]
        public class Command : IRequest<Pool>
        {
            [JsonIgnore]
            public Guid Id { get; set; }

            /// <summary>
            /// The Name of the Pool
            /// </summary>
            [DataMember]
            public string Name { get; set; }
        }

        public class Validator : AbstractValidator<Command>
        {
            public Validator(IValidationService validationService)
            {
                RuleFor(x => x.Id).PoolExists(validationService);
            }
        }

        public class Handler : IRequestHandler<Command, Pool>
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

            public async Task<Pool> Handle(Command command, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                var pool = await _db.Pools
                    .Where(x => x.Id == command.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                _mapper.Map(command, pool);
                await _db.SaveChangesAsync();

                return _mapper.Map<Pool>(pool);
            }
        }
    }
}
