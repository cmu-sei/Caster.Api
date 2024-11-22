// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using AutoMapper;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Linq;
using Caster.Api.Infrastructure.Authorization;
using Microsoft.EntityFrameworkCore;
using FluentValidation;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

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

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Command, Vlan>
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ManageVLANs], cancellationToken);

            public override async Task<Vlan> HandleRequest(Command command, CancellationToken cancellationToken)
            {
                var vlan = await dbContext.Vlans
                    .Where(x => x.Id == command.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                mapper.Map(command, vlan);
                await dbContext.SaveChangesAsync(cancellationToken);

                return mapper.Map<Vlan>(vlan);
            }
        }
    }
}

