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
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Domain.Services;
using System.Linq;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Domain.Events;

namespace Caster.Api.Features.Runs
{
    public class Reject
    {
        [DataContract(Name="RejectRunCommand")]
        public class Command : IRequest<Run>
        {
            /// <summary>
            /// The Id of the Run to reject
            /// </summary>
            [DataMember]
            public Guid Id { get; set; }
        }

        public class Handler : IRequestHandler<Command, Run>
        {
            private readonly CasterContext _db;
            private readonly IMapper _mapper;
            private readonly TerraformOptions _terraformOptions;
            private readonly IAuthorizationService _authorizationService;
            private readonly ClaimsPrincipal _user;
            private readonly ILockService _lockService;
            private readonly IMediator _mediator;

            public Handler(
                CasterContext db,
                IMapper mapper,
                TerraformOptions terraformOptions,
                IAuthorizationService authorizationService,
                IIdentityResolver identityResolver,
                ILockService lockService,
                IMediator mediator)
            {
                _db = db;
                _mapper = mapper;
                _terraformOptions = terraformOptions;
                _authorizationService = authorizationService;
                _user = identityResolver.GetClaimsPrincipal();
                _lockService = lockService;
                _mediator = mediator;
            }

            public async Task<Run> Handle(Command request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                var workspaceId = await _db.Runs.Where(r => r.Id == request.Id).Select(r => r.WorkspaceId).FirstOrDefaultAsync();

                using (var lockResult = await _lockService.GetWorkspaceLock(workspaceId).LockAsync(0))
                {
                    if (!lockResult.AcquiredLock)
                        throw new WorkspaceConflictException();

                    var run = await _db.Runs
                        .Include(r => r.Plan)
                        .Include(r => r.Apply)
                        .Include(r => r.Workspace)
                        .FirstOrDefaultAsync(r => r.Id == request.Id);

                    ValidateRun(run);

                    run.Workspace.CleanupFileSystem(_terraformOptions.RootWorkingDirectory);

                    run.Status = RunStatus.Rejected;

                    if (run.Plan != null) {
                        run.Plan.Status = PlanStatus.Rejected;
                    }

                    await _db.SaveChangesAsync();

                    await _mediator.Publish(new RunUpdated(run.Id));

                    return _mapper.Map<Run>(run);
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

