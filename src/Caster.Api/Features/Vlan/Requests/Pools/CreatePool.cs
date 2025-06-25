// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using Caster.Api.Infrastructure.Authorization;
using System.Linq;
using System.ComponentModel;
using EFCore.BulkExtensions;
using System.Collections.Generic;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Vlan
{
    public class CreatePool
    {
        [DataContract(Name = "CreatePoolCommand")]
        public class Command : IRequest<Pool>
        {
            /// <summary>
            /// The Name of the Pool to create
            /// </summary>
            [DataMember]
            public string Name { get; set; }

            /// <summary>
            /// The Vlan Ids to reserve, preventing their use
            /// </summary>
            [DataMember]
            [DefaultValue(new int[] { 0, 1, 4095 })]
            public int[] ReservedVlanIds { get; set; } = [0, 1, 4095];
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Command, Pool>
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ManageVLANs], cancellationToken);

            public override async Task<Pool> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var pool = mapper.Map<Domain.Models.Pool>(request);
                await dbContext.CreateVlanPool(pool, request.ReservedVlanIds, true, cancellationToken);
                return mapper.Map<Pool>(pool);
            }
        }
    }
}
