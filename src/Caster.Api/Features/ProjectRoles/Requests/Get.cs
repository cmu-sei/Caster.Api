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
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.ProjectRoles
{
    public class Get
    {
        [DataContract(Name = "GetProjectRoleQuery")]
        public class Query : IRequest<ProjectRole>
        {
            /// <summary>
            /// The Id of the ProjectRole to retrieve
            /// </summary>
            [DataMember]
            public Guid Id { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, ProjectRole>
        {
            public override async Task Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ViewRoles], cancellationToken);

            public override async Task<ProjectRole> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                var projectRole = await dbContext.ProjectRoles
                    .ProjectTo<ProjectRole>(mapper.ConfigurationProvider, dest => dest.Permissions)
                    .SingleOrDefaultAsync(e => e.Id == request.Id, cancellationToken);

                if (projectRole == null)
                    throw new EntityNotFoundException<ProjectRole>();

                return projectRole;
            }
        }
    }
}

