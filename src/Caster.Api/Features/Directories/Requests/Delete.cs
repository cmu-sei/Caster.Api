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
            public override async Task Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Directory>(request.Id, [SystemPermissions.EditProjects], [ProjectPermissions.EditProject], cancellationToken);

            public override async Task HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var directory = await dbContext.Directories.FindAsync(request.Id);

                if (directory == null)
                    throw new EntityNotFoundException<Directory>();

                var workspaces = await CheckForResources(directory);

                if (workspaces.Any())
                {
                    string errorMessage = "Cannot delete this Directory due to existing Resources in the following Workspaces:";

                    foreach (var workspace in workspaces)
                    {
                        errorMessage += $"\n Name: {workspace.Name}, Id: {workspace.Id} in Directory: {workspace.Directory.Name}, {workspace.DirectoryId}";
                    }

                    throw new ConflictException(errorMessage);
                }

                dbContext.Directories.Remove(directory);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            private async Task<Workspace[]> CheckForResources(Domain.Models.Directory directory)
            {
                var directories = await dbContext.Directories
                    .GetChildren(directory, true)
                    .Include(d => d.Workspaces)
                    .ToArrayAsync();

                List<Workspace> workspaces = new List<Workspace>();

                foreach (var workspace in directories.SelectMany(d => d.Workspaces))
                {
                    if (workspace.GetState().GetResources().Any())
                    {
                        workspaces.Add(workspace);
                    }
                }

                return workspaces.ToArray();
            }
        }
    }
}
