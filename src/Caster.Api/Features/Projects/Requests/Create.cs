// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

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
using Microsoft.Extensions.Logging;
using Npgsql;
using System;
using Caster.Api.Domain.Services;

namespace Caster.Api.Features.Projects
{
    public class Create
    {
        [DataContract(Name = "CreateProjectCommand")]
        public class Command : IRequest<Project>
        {
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
            IIdentityResolver identityResolver) : BaseHandler<Command, Project>
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.CreateProjects], cancellationToken);

            public override async Task<Project> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                // Validate required fields
                if (string.IsNullOrWhiteSpace(request.Name))
                    throw new ArgumentException("Project Name is required and cannot be empty.");

                var project = mapper.Map<Domain.Models.Project>(request);
                dbContext.Projects.Add(project);

                // Add the creator as a member with the appropriate role
                var projectMembership = new Domain.Models.ProjectMembership();
                projectMembership.UserId = identityResolver.GetClaimsPrincipal().GetId();
                projectMembership.Project = project;
                projectMembership.RoleId = ProjectRoleDefaults.ProjectCreatorRoleId;
                dbContext.ProjectMemberships.Add(projectMembership);

                try
                {
                    await dbContext.SaveChangesAsync(cancellationToken);


                    return mapper.Map<Project>(project);
                }
                catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx)
                {

                    // Handle specific PostgreSQL errors
                    switch (pgEx.SqlState)
                    {
                        case "23505": // unique_violation
                            throw new InvalidOperationException($"A Project with the ID '{project.Id}' already exists.", ex);
                        case "23503": // foreign_key_violation
                            var constraintName = pgEx.ConstraintName ?? "unknown";
                            throw new InvalidOperationException($"Foreign key constraint violated: {constraintName}. Please verify all referenced entities exist.", ex);
                        case "23514": // check_violation
                            throw new InvalidOperationException($"Data validation failed: {pgEx.MessageText}", ex);
                        default:
                            throw new InvalidOperationException($"Database error creating Project: {pgEx.MessageText}", ex);
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"An unexpected error occurred while creating the Project: {ex.Message}", ex);
                }
            }
        }
    }
}
