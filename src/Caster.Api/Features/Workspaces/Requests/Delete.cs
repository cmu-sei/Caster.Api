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
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Domain.Services;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Shared;

namespace Caster.Api.Features.Workspaces
{
    public class Delete
    {
        [DataContract(Name = "DeleteWorkspaceCommand")]
        public class Command : IRequest
        {
            public Guid Id { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext, ILockService lockService) : BaseHandler<Command>
        {
            public override async Task Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Domain.Models.Workspace>(request.Id, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

            public override async Task HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var workspace = await dbContext.Workspaces.FindAsync([request.Id], cancellationToken);

                if (workspace == null)
                    throw new EntityNotFoundException<Workspace>();

                using (var lockResult = await lockService.GetWorkspaceLock(request.Id).LockAsync(0))
                {
                    if (!lockResult.AcquiredLock)
                        throw new WorkspaceConflictException();

                    if (workspace.GetState().GetResources().Any())
                        throw new ConflictException("Cannot delete a Workspace with deployed Resources.");

                    if (await dbContext.AnyIncompleteRuns(request.Id))
                        throw new ConflictException("Cannot delete a Workspace with pending Runs.");

                    dbContext.Workspaces.Remove(workspace);
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
            }
        }
    }
}
