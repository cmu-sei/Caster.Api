// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using Microsoft.EntityFrameworkCore;
using AutoMapper.QueryableExtensions;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Workspaces
{
    public class GetAll
    {
        [DataContract(Name = "GetWorkspacesQuery")]
        public class Query : IRequest<Workspace[]>
        {
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, Workspace[]>
        {
            public override async Task Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ViewProjects], cancellationToken);

            public override async Task<Workspace[]> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                return await dbContext.Workspaces
                    .ProjectTo<Workspace>(mapper.ConfigurationProvider)
                    .ToArrayAsync();
            }
        }
    }
}

