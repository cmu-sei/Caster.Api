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
using Caster.Api.Infrastructure.Options;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Domain.Services;
using System.Linq;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Domain.Events;
using AutoMapper.QueryableExtensions;
using Caster.Api.Features.Shared;

namespace Caster.Api.Features.Runs
{
    public class Reject
    {
        [DataContract(Name = "RejectRunCommand")]
        public class Command : IRequest<Run>
        {
            /// <summary>
            /// The Id of the Run to reject
            /// </summary>
            [DataMember]
            public Guid Id { get; set; }
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
                await authorizationService.Authorize<Domain.Models.Run>(request.Id, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

            public override async Task<Run> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var workspaceId = await dbContext.Runs
                    .Where(r => r.Id == request.Id)
                    .Select(r => r.WorkspaceId)
                    .FirstOrDefaultAsync(cancellationToken);

                using (var lockResult = await lockService.GetWorkspaceLock(workspaceId).LockAsync(0))
                {
                    if (!lockResult.AcquiredLock)
                        throw new WorkspaceConflictException();

                    var run = await dbContext.Runs
                        .Include(r => r.Plan)
                        .Include(r => r.Apply)
                        .Include(r => r.Workspace)
                        .FirstOrDefaultAsync(r => r.Id == request.Id);

                    ValidateRun(run);

                    run.Workspace.CleanupFileSystem(terraformOptions.RootWorkingDirectory);

                    run.Status = RunStatus.Rejected;

                    if (run.Plan != null)
                    {
                        run.Plan.Status = PlanStatus.Rejected;
                    }

                    run.Modify(identityResolver.GetId());
                    await dbContext.SaveChangesAsync();
                    await mediator.Publish(new RunUpdated(run.Id));

                    return await dbContext.Runs
                        .ProjectTo<Run>(mapper.ConfigurationProvider)
                        .SingleOrDefaultAsync(x => x.Id == run.Id, cancellationToken);
                }
            }

            private void ValidateRun(Domain.Models.Run run)
            {
                if (run == null)
                    throw new EntityNotFoundException<Run>();

                if (run.Plan == null || run.Plan.Status == PlanStatus.Queued || run.Plan.Status == PlanStatus.Planning)
                {
                    throw new InvalidOperationException("Cannot reject a Run with a Plan in progress. Please try again when it has completed.");
                }

                if (run.Apply != null)
                    throw new ConflictException("Cannot reject a Run that is already being applied");
            }
        }
    }
}
