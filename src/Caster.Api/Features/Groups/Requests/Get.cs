// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using AutoMapper.QueryableExtensions;
using System.Runtime.Serialization;
using Caster.Api.Data;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Groups
{
    public class Get
    {
        [DataContract(Name = "GetGroupQuery")]
        public class Query : IRequest<Group>
        {
            /// <summary>
            /// The Id of the Group to retrieve
            /// </summary>
            [DataMember]
            public Guid Id { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, Group>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ViewGroups], cancellationToken);

            public override async Task<Group> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                var group = await dbContext.Groups
                    .ProjectTo<Group>(mapper.ConfigurationProvider)
                    .SingleOrDefaultAsync(e => e.Id == request.Id);

                if (group == null)
                    throw new EntityNotFoundException<Group>();

                return group;
            }
        }
    }
}
