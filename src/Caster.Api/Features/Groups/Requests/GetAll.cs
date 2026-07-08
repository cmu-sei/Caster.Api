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
using System.Linq;

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
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken)
            {
                if (await authorizationService.Authorize([SystemPermission.ViewGroups, SystemPermission.ViewProjects], cancellationToken))
                {
                    return true;
                }

                if (authorizationService.
                    GetProjectPermissions()
                    .Any(x => x.Permissions.Contains(ProjectPermission.ManageProject)))
                {
                    return true;
                }

                return authorizationService.
                    GetGroupPermissions()
                    .Any(x => x.Permissions.Contains(GroupPermission.ManageMembership));
            }

            public override async Task<Group[]> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                var isSystemViewer =
                    await authorizationService.Authorize([SystemPermission.ViewGroups, SystemPermission.ViewProjects], cancellationToken) ||
                    authorizationService.GetProjectPermissions().Any(x => x.Permissions.Contains(ProjectPermission.ManageProject));

                var query = dbContext.Groups.AsQueryable();

                if (!isSystemViewer)
                {
                    // The user only reached this point because they manage one or more groups,
                    // so scope the result to just those groups.
                    var managedIds = authorizationService.GetGroupPermissions()
                        .Where(x => x.Permissions.Contains(GroupPermission.ManageMembership))
                        .Select(x => x.GroupId)
                        .ToList();
                    query = query.Where(g => managedIds.Contains(g.Id));
                }

                return await query
                    .ProjectTo<Group>(mapper.ConfigurationProvider)
                    .ToArrayAsync(cancellationToken);
            }
        }
    }
}
