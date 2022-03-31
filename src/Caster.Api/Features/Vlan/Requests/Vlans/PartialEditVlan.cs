// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using AutoMapper;
using System.Runtime.Serialization;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Identity;
using CodeAnalysis = Microsoft.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Security.Claims;
using System.Linq;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Microsoft.EntityFrameworkCore;
using FluentValidation;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;

namespace Caster.Api.Features.Vlan
{
    public class PartialEditVlan
    {
        [DataContract(Name = "PartialEditVlanCommand")]
        public class Command : IRequest<Vlan>
        {
            [JsonIgnore]
            public Guid Id { get; set; }

            /// <summary>
            /// If true, this VLAN cannot be used
            /// </summary>
            public bool? Reserved { get; set; }

            /// <summary>
            /// Tag to set on this VLAN
            /// </summary>
            [DataMember]
            public string Tag { get; set; }
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


            public async Task<Vlan> Handle(Command command, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                var vlan = await _db.Vlans
                    .Where(x => x.Id == command.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                _mapper.Map(command, vlan);
                await _db.SaveChangesAsync(cancellationToken);

                return _mapper.Map<Vlan>(vlan);
            }
        }
    }
}

