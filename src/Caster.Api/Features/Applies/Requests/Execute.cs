// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Runtime.Serialization;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Domain.Events;
using System.Security.Claims;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Domain.Services;
using Caster.Api.Domain.Services.Capacity;
using System.Linq;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Features.Shared;

namespace Caster.Api.Features.Applies
{
    public class Execute
    {
        [DataContract(Name = "ApplyRunCommand")]
        public class Command : IRequest<Apply>
        {
            /// <summary>
            /// Id of the Run whose Plan is to be Applied
            /// </summary>
            [DataMember]
            public Guid RunId { get; set; }

            /// <summary>
            /// True to apply even though the hypervisors may not have room for
            /// the run. Never lets a run over a limit through.
            /// </summary>
            [DataMember]
            public bool AcknowledgeCapacityWarning { get; set; }
        }

        public class Handler : BaseHandler<Command, Apply>
        {
            private readonly CasterContext _db;
            private readonly IMediator _mediator;
            private readonly ICasterAuthorizationService _authorizationService;
            private readonly ClaimsPrincipal _user;
            private readonly ILockService _lockService;
            private readonly ICapacityCheckService _capacity;

            public Handler(
                CasterContext db,
                IMediator mediator,
                ICasterAuthorizationService authorizationService,
                IIdentityResolver identityResolver,
                ILockService lockService,
                ICapacityCheckService capacity)
            {
                _capacity = capacity;
                _db = db;
                _mediator = mediator;
                _authorizationService = authorizationService;
                _user = identityResolver.GetClaimsPrincipal();
                _lockService = lockService;
            }

            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await _authorizationService.Authorize<Run>(request.RunId, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

            public override async Task<Apply> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var workspaceId = await _db.Runs.Where(r => r.Id == request.RunId).Select(r => r.WorkspaceId).FirstOrDefaultAsync();

                Domain.Models.Apply apply = null;

                using (var lockResult = await _lockService.GetWorkspaceLock(workspaceId).LockAsync(0))
                {
                    if (!lockResult.AcquiredLock)
                        throw new WorkspaceConflictException();

                    var run = await _db.Runs
                        .Include(r => r.Plan)
                        .Include(r => r.Apply)
                        .SingleOrDefaultAsync(r => r.Id == request.RunId);

                    ValidateRun(run);
                    var capacityWarning = await CheckCapacity(request, run, cancellationToken);

                    apply = new Domain.Models.Apply
                    {
                        RunId = run.Id,
                        Status = ApplyStatus.Queued,
                        CapacityWarning = capacityWarning
                    };

                    await _db.Applies.AddAsync(apply);
                    run.Status = RunStatus.ApplyQueued;
                    run.Modify(_user.GetId());
                    await _db.SaveChangesAsync();
                }

                await _mediator.Publish(new ApplyCreated { ApplyId = apply.Id, RunId = apply.RunId, WorkspaceId = workspaceId });
                await _mediator.Publish(new RunUpdated(apply.RunId));
                return apply.ToApply();
            }

            /// <summary>
            /// Refuses a run over an effective limit, or out of room with the room
            /// check set to Block. A run out of room with it set to Warn is applied,
            /// so an automated caller such as Alloy is never stopped by it, and the
            /// warning is kept on the apply, saying whether it was acknowledged;
            /// Caster's own Apply asks for the acknowledgement first.
            /// </summary>
            private async Task<string> CheckCapacity(Command request, Run run, CancellationToken cancellationToken)
            {
                var check = await _capacity.CheckRunAsync(run.Id, cancellationToken);

                if (check == null)
                    return null;

                if (!check.CanApply)
                    throw new ConflictException($"This run cannot be applied. {check.Message}");

                if (!check.RequiresAcknowledgement)
                    return null;

                return request.AcknowledgeCapacityWarning
                    ? $"{check.Message} Applied after the warning was acknowledged."
                    : $"{check.Message} Applied without acknowledging the warning.";
            }

            private void ValidateRun(Run run)
            {
                if (run == null)
                    throw new EntityNotFoundException<Run>();

                if (run.Plan == null || run.Plan.Status != PlanStatus.Planned)
                    throw new ConflictException("Run have must a completed Plan to Apply");

                if (run.Apply != null)
                    throw new ConflictException("Run already has an Apply");
            }
        }
    }
}
