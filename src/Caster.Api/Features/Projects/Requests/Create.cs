// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Runtime.Serialization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        }

        public class Handler(
            ICasterAuthorizationService authorizationService,
            IMapper mapper,
            CasterContext dbContext,
            TelemetryService telemetryService,
            IIdentityResolver identityResolver,
            ILogger<Handler> logger) : BaseHandler<Command, Project>
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.CreateProjects], cancellationToken);

            public override async Task<Project> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var project = mapper.Map<Domain.Models.Project>(request);

                // Allow Blueprint (or other callers) to specify the ID
                if (request.Id.HasValue)
                {
                    project.Id = request.Id.Value;
                }

                logger.LogInformation("Creating project with ID: {ProjectId}, Name: {ProjectName}", project.Id, project.Name);
                dbContext.Projects.Add(project);

                // Add the creator as a member with the appropriate role
                var projectMembership = new Domain.Models.ProjectMembership();
                projectMembership.UserId = identityResolver.GetClaimsPrincipal().GetId();
                projectMembership.Project = project;
                projectMembership.RoleId = ProjectRoleDefaults.ProjectCreatorRoleId;
                dbContext.ProjectMemberships.Add(projectMembership);

                logger.LogInformation("Saving project to database...");
                var result = await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogInformation("SaveChanges completed. Affected rows: {RowCount}", result);

                var savedProject = mapper.Map<Project>(project);
                logger.LogInformation("Returning project with ID: {ProjectId}", savedProject.Id);
                return savedProject;
            }
        }
    }
}
