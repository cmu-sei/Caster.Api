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
using System.Collections.Generic;

namespace Caster.Api.Features.Vlan
{
    public class ReserveVlans
    {
        [DataContract(Name = "ReserveVlansCommand")]
        public class Command : IRequest<ReserveVlansResult>
        {
            /// <summary>
            /// If true, this VLAN cannot be used
            /// </summary>
            public bool Reserved { get; set; }

            /// <summary>
            /// The VLANs to reserve
            /// </summary>
            public Guid[] VlanIds { get; set; }
        }

        public class ReserveVlansResult
        {
            public bool Success => NotUpdated.Count() == 0;
            public IEnumerable<Guid> Updated { get; set; }
            public IEnumerable<Guid> NotUpdated { get; set; }
        }

        public class Validator : AbstractValidator<Command>
        {
            public Validator()
            {
                RuleFor(x => x.VlanIds).NotEmpty();
            }
        }

        public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext) : BaseHandler<Command, ReserveVlansResult>
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ManageVLANs], cancellationToken);

            public override async Task<ReserveVlansResult> HandleRequest(Command command, CancellationToken cancellationToken)
            {
                var vlans = await dbContext.Vlans
                    .Where(x => command.VlanIds.Contains(x.Id))
                    .ToArrayAsync(cancellationToken);

                var updated = new List<Guid>();
                var notUpdated = new List<Guid>();

                foreach (var vlan in vlans)
                {
                    try
                    {
                        vlan.Reserved = command.Reserved;
                        updated.Add(vlan.Id);
                    }
                    catch (Exception)
                    {
                        notUpdated.Add(vlan.Id);
                    }
                }

                await dbContext.SaveChangesAsync(cancellationToken);

                return new ReserveVlansResult
                {
                    NotUpdated = notUpdated,
                    Updated = updated
                };
            }
        }
    }
}

