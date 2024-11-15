// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using AutoMapper.QueryableExtensions;
using Caster.Api.Data;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Groups
{
    public class GetAll
    {
        [DataContract(Name = "GetGroupsQuery")]
        public class Query : IRequest<Group[]>
        {
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, Group[]>
        {
            public override async Task Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermissions.ViewGroups], cancellationToken);

            public override async Task<Group[]> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                return await dbContext.Groups
                    .ProjectTo<Group>(mapper.ConfigurationProvider)
                    .ToArrayAsync();
            }
        }
    }
}
