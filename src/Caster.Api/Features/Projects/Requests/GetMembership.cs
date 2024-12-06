// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using AutoMapper;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using Microsoft.EntityFrameworkCore;
using AutoMapper.QueryableExtensions;
using Caster.Api.Infrastructure.Authorization;
using FluentValidation;
using System.Linq;
using System.Text.Json.Serialization;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Projects
{
    public class GetMembership
    {
        [DataContract(Name = "GetProjectMembershipQuery")]
        public record Query : IRequest<ProjectMembership>
        {
            /// <summary>
            /// Id of the ProjectMembership
            /// </summary>
            [JsonIgnore]
            public Guid Id { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, ProjectMembership>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Domain.Models.ProjectMembership>(request.Id, [SystemPermission.ManageProjects], [ProjectPermission.ManageProject], cancellationToken);

            public override async Task<ProjectMembership> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                var projectMembership = await dbContext.ProjectMemberships
                    .Where(x => x.Id == request.Id)
                    .ProjectTo<ProjectMembership>(mapper.ConfigurationProvider)
                    .FirstOrDefaultAsync(cancellationToken);

                if (projectMembership == null)
                    throw new EntityNotFoundException<ProjectMembership>();

                return projectMembership;
            }
        }
    }
}
