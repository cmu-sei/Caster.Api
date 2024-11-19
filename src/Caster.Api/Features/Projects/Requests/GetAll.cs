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
using Azure.Core;
using System.Linq;

namespace Caster.Api.Features.Projects
{
    public class GetAll
    {
        [DataContract(Name = "GetProjectsQuery")]
        public class Query : IRequest<Project[]>
        {
            [DataMember]
            public bool OnlyMine { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, Project[]>
        {
            public override async Task Authorize(Query request, CancellationToken cancellationToken)
            {
                if (!request.OnlyMine)
                    await authorizationService.Authorize([SystemPermissions.ViewProjects], cancellationToken);
            }

            public override async Task<Project[]> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                var query = dbContext.Projects.AsQueryable();

                if (request.OnlyMine)
                {
                    var projectIds = authorizationService.GetAuthorizedProjectIds();
                    query = query.Where(x => projectIds.Contains(x.Id));
                }

                return await query
                    .ProjectTo<Project>(mapper.ConfigurationProvider)
                    .ToArrayAsync(cancellationToken);
            }
        }
    }
}
