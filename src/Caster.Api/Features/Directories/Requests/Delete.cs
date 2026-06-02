// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Directories.Interfaces;
using Caster.Api.Data.Extensions;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Directories
{
    public class Delete
    {
        [DataContract(Name = "DeleteDirectoryCommand")]
        public class Command : IRequest, IDirectoryDeleteRequest
        {
            public Guid Id { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext) : BaseHandler<Command>
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Domain.Models.Directory>(request.Id, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

            public override async Task HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var directory = await dbContext.Directories.FindAsync(request.Id);

                if (directory == null)
                    throw new EntityNotFoundException<Directory>();

                var (workspacesWithResources, workspacesWithRuns) = await CheckForResourcesAndRuns(directory);

                if (workspacesWithResources.Any())
                {
                    string errorMessage = "Cannot delete this Directory due to existing Resources in the following Workspaces:";
                    foreach (var workspace in workspacesWithResources)
                    {
                        errorMessage += $"\n Workspace Id: {workspace.Id}, Directory Id: {workspace.DirectoryId}";
                    }
                    throw new ConflictException(errorMessage);
                }

                if (workspacesWithRuns.Any())
                {
                    string errorMessage = "Cannot delete this Directory due to pending Runs in the following Workspaces:";
                    foreach (var workspace in workspacesWithRuns)
                    {
                        errorMessage += $"\n Workspace Id: {workspace.Id}, Directory Id: {workspace.DirectoryId}";
                    }
                    throw new ConflictException(errorMessage);
                }

                dbContext.Directories.Remove(directory);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            private async Task<(Workspace[], Workspace[])> CheckForResourcesAndRuns(Domain.Models.Directory directory)
            {
                var directories = await dbContext.Directories
                    .GetChildren(directory, true)
                    .Include(d => d.Workspaces)
                    .ToArrayAsync();

                List<Workspace> workspacesWithResources = new List<Workspace>();
                List<Workspace> workspacesWithRuns = new List<Workspace>();

                foreach (var workspace in directories.SelectMany(d => d.Workspaces))
                {
                    if (workspace.GetState().GetResources().Any())
                        workspacesWithResources.Add(workspace);

                    if (await dbContext.AnyIncompleteRuns(workspace.Id))
                        workspacesWithRuns.Add(workspace);
                }

                return (workspacesWithResources.ToArray(), workspacesWithRuns.ToArray());
            }
        }
    }
}
