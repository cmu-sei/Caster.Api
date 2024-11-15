// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Runtime.Serialization;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Domain.Events;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Domain.Services;
using System.Linq;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Infrastructure.Options;
using AutoMapper.QueryableExtensions;
using Caster.Api.Features.Shared;

namespace Caster.Api.Features.Runs
{
    public class SaveState
    {
        [DataContract(Name = "SaveStateCommand")]
        public class Command : IRequest<Run>
        {
            /// <summary>
            /// Id of the Run whose State is to be saved
            /// </summary>
            [DataMember]
            public Guid RunId { get; set; }
        }

        public class Handler(
             ICasterAuthorizationService authorizationService,
             IMapper mapper,
             CasterContext dbContext,
             TerraformOptions terraformOptions,
             ILockService lockService,
             IMediator mediator,
             IIdentityResolver identityResolver) : BaseHandler<Command, Run>
        {
            public override async Task Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Run>(request.RunId, [SystemPermissions.EditProjects], [ProjectPermissions.EditProject], cancellationToken);

            public override async Task<Run> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var run = await dbContext.Runs
                    .Include(r => r.Apply)
                    .Include(r => r.Workspace)
                    .Where(r => r.Id == request.RunId)
                    .FirstOrDefaultAsync(cancellationToken);

                if (run == null)
                    throw new EntityNotFoundException<Run>();

                using (var lockResult = await lockService.GetWorkspaceLock(run.WorkspaceId).LockAsync(0))
                {
                    if (!lockResult.AcquiredLock)
                        throw new WorkspaceConflictException();

                    await ValidateRun(run, cancellationToken);

                    var workingDir = run.Workspace.GetPath(terraformOptions.RootWorkingDirectory);
                    var stateRetrieved = await run.Workspace.RetrieveState(workingDir);

                    if (stateRetrieved)
                    {
                        run.Apply.Status = run.Apply.Status == ApplyStatus.Applied_StateError ? ApplyStatus.Applied : ApplyStatus.Failed;
                        run.Status = run.Status == RunStatus.Applied_StateError ? RunStatus.Applied : RunStatus.Failed;

                        await dbContext.SaveChangesAsync(cancellationToken);
                        await mediator.Publish(new RunUpdated(run.Id));
                        await mediator.Publish(new ApplyCompleted(run.Workspace));
                        run.Workspace.CleanupFileSystem(terraformOptions.RootWorkingDirectory);
                    }
                }

                return await dbContext.Runs
                    .ProjectTo<Run>(mapper.ConfigurationProvider)
                    .SingleOrDefaultAsync(x => x.Id == run.Id, cancellationToken);
            }

            private async Task ValidateRun(Domain.Models.Run run, CancellationToken cancellationToken)
            {
                if (run.Status != RunStatus.Applied_StateError && run.Status != RunStatus.Failed_StateError)
                    throw new WorkspaceConflictException();

                var notLatest = await dbContext.Runs
                    .AnyAsync(r =>
                        r.WorkspaceId == run.WorkspaceId &&
                        r.CreatedAt > run.CreatedAt,
                        cancellationToken);

                if (notLatest)
                    throw new ConflictException("State can only be saved for the latest Run.");
            }
        }
    }
}
