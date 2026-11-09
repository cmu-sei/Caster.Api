// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Runtime.Serialization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Infrastructure.Exceptions;
using MediatR;
using AutoMapper;
using System.Collections.Generic;
using Caster.Api.Data;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Shared;
using Microsoft.EntityFrameworkCore;
using Caster.Api.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Caster.Api.Features.Projects
{
    public class Create
    {
        [DataContract(Name = "CreateProjectCommand")]
        public class Command : IRequest<Project>
        {
            /// <summary>
            /// Optional ID for the project. If not provided, one will be generated.
            /// </summary>
            [DataMember]
            public Guid? Id { get; set; }

            /// <summary>
            /// Name of the project.
            /// </summary>
            [DataMember]
            public string Name { get; set; }

            /// <summary>
            /// Description of the project.
            /// </summary>
            [DataMember]
            public string Description { get; set; }

            /// <summary>
            /// The project group to create the project in, if any. Needs ManageProjectGroups or
            /// being a manager of the group, as well as CreateProjects.
            /// </summary>
            [DataMember]
            public Guid? ProjectGroupId { get; set; }
        }

        public class Handler(
            ICasterAuthorizationService authorizationService,
            ProjectGroups.ProjectGroupAccess projectGroupAccess,
            IMapper mapper,
            CasterContext dbContext,
            TelemetryService telemetryService,
            IIdentityResolver identityResolver,
            ILogger<Handler> logger) : BaseHandler<Command, Project>
        {
            /// <summary>
            /// Checked before the project exists: ManageProject on it isn't, since the creator gets
            /// it with the project.
            /// </summary>
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.CreateProjects], cancellationToken) &&
                (!request.ProjectGroupId.HasValue || projectGroupAccess.CanManage(request.ProjectGroupId.Value));

            public override async Task<Project> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var project = mapper.Map<Domain.Models.Project>(request);
                project.DateCreated = DateTime.UtcNow;

                // Allow Blueprint (or other callers) to specify the ID
                if (request.Id.HasValue)
                {
                    project.Id = request.Id.Value;
                }

                if (request.ProjectGroupId.HasValue &&
                    !await dbContext.ProjectGroups.AnyAsync(x => x.Id == request.ProjectGroupId.Value, cancellationToken))
                {
                    throw new EntityNotFoundException<Domain.Models.ProjectGroup>();
                }

                logger.LogInformation("Creating project with ID: {ProjectId}", project.Id);
                dbContext.Projects.Add(project);

                // Add the creator as a member with the appropriate role
                var projectMembership = new Domain.Models.ProjectMembership();
                projectMembership.UserId = identityResolver.GetClaimsPrincipal().GetId();
                projectMembership.Project = project;
                projectMembership.RoleId = ProjectRoleDefaults.ProjectCreatorRoleId;
                dbContext.ProjectMemberships.Add(projectMembership);

                await dbContext.SaveChangesAsync(cancellationToken);

                return mapper.Map<Project>(project);
            }
        }
    }
}
