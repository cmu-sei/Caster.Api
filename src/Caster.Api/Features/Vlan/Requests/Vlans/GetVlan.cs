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
using AutoMapper.QueryableExtensions;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Shared;

namespace Caster.Api.Features.Vlan
{
    public class GetVlan
    {
        [DataContract(Name = "GetVlanQuery")]
        public class Query : IRequest<Vlan>
        {
            /// <summary>
            /// The Id of the VLAN being requested
            /// </summary>
            [DataMember]
            public Guid Id { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, Vlan>
        {
            public override async Task Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ViewVLANs], cancellationToken);

            public override async Task<Vlan> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                var vlan = await dbContext.Vlans
                    .Where(x => x.Id == request.Id)
                    .ProjectTo<Vlan>(mapper.ConfigurationProvider)
                    .FirstOrDefaultAsync(cancellationToken);

                if (vlan == null)
                    throw new EntityNotFoundException<Vlan>();

                return vlan;
            }
        }
    }
}
