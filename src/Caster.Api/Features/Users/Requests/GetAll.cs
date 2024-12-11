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
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;
using System.Linq;

namespace Caster.Api.Features.Users
{
    public class GetAll
    {
        [DataContract(Name = "GetUsersQuery")]
        public class Query : IRequest<User[]>
        {
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, User[]>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken)
            {
                if (await authorizationService.Authorize([SystemPermission.ViewUsers, SystemPermission.ViewProjects], cancellationToken))
                {
                    return true;
                }

                return authorizationService.
                    GetProjectPermissions()
                    .Any(x => x.Permissions.Contains(ProjectPermission.ManageProject));
            }

            public override async Task<User[]> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                return await dbContext.Users
                    .ProjectTo<User>(mapper.ConfigurationProvider)
                    .ToArrayAsync(cancellationToken);
            }
        }
    }
}

