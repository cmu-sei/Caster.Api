// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using AutoMapper.QueryableExtensions;
using System.Runtime.Serialization;
using Caster.Api.Data;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;
using System.Linq;

namespace Caster.Api.Features.ProjectRoles
{
    public class GetAll
    {
        [DataContract(Name = "GetProjectRolesQuery")]
        public class Query : IRequest<ProjectRole[]>
        {
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, ProjectRole[]>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken)
            {
                if (await authorizationService.Authorize([SystemPermission.ViewRoles], cancellationToken))
                {
                    return true;
                }

                return authorizationService.
                    GetProjectPermissions()
                    .Any(x => x.Permissions.Contains(ProjectPermission.ManageProject));
            }

            public override async Task<ProjectRole[]> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                return await dbContext.ProjectRoles
                    .ProjectTo<ProjectRole>(mapper.ConfigurationProvider)
                    .ToArrayAsync(cancellationToken);
            }
        }
    }
}

