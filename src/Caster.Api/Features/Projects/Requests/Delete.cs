// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using Caster.Api.Infrastructure.Exceptions;
using System.Runtime.Serialization;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using AutoMapper.QueryableExtensions;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;

namespace Caster.Api.Features.Projects
{
    public class Delete
    {
        [DataContract(Name = "DeleteProjectCommand")]
        public class Command : IRequest
        {
            public Guid Id { get; set; }
        }

        public class Handler(
            ICasterAuthorizationService authorizationService,
            TelemetryService telemetryService,
            CasterContext dbContext) : BaseHandler<Command>
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Domain.Models.Project>(request.Id, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

            public override async Task HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var project = await dbContext.Projects.FindAsync([request.Id], cancellationToken);

                if (project == null)
                    throw new EntityNotFoundException<Project>();

                // Check all workspaces in this project for deployed resources
                var workspaces = await dbContext.Workspaces
                    .Include(w => w.Directory)
                    .Where(w => w.Directory.ProjectId == request.Id)
                    .ToListAsync(cancellationToken);

                var workspacesWithResources = new List<Workspace>();
                var workspacesWithRuns = new List<Workspace>();

                foreach (var workspace in workspaces)
                {
                    if (workspace.GetState().GetResources().Any())
                        workspacesWithResources.Add(workspace);

                    if (await dbContext.AnyIncompleteRuns(workspace.Id))
                        workspacesWithRuns.Add(workspace);
                }

                if (workspacesWithResources.Any())
                    throw new ConflictException("Cannot delete a Project with deployed Resources.");

                if (workspacesWithRuns.Any())
                    throw new ConflictException("Cannot delete a Project with pending Runs.");

                dbContext.Projects.Remove(project);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
