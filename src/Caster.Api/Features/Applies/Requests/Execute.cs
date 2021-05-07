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
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Domain.Services;
using System.Linq;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Infrastructure.Extensions;

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
        }

        public class Handler : IRequestHandler<Command, Apply>
        {
            private readonly CasterContext _db;
            private readonly IMapper _mapper;
            private readonly IMediator _mediator;
            private readonly IAuthorizationService _authorizationService;
            private readonly ClaimsPrincipal _user;
            private readonly ILockService _lockService;

            public Handler(
                CasterContext db,
                IMapper mapper,
                IMediator mediator,
                IAuthorizationService authorizationService,
                IIdentityResolver identityResolver,
                ILockService lockService)
            {
                _db = db;
                _mapper = mapper;
                _mediator = mediator;
                _authorizationService = authorizationService;
                _user = identityResolver.GetClaimsPrincipal();
                _lockService = lockService;
            }

            public async Task<Apply> Handle(Command request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

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

                    apply = new Domain.Models.Apply
                    {
                        RunId = run.Id,
                        Status = ApplyStatus.Queued
                    };

                    await _db.Applies.AddAsync(apply);
                    run.Modify(_user.GetId());
                    await _db.SaveChangesAsync();
                }

                await _mediator.Publish(new ApplyCreated { ApplyId = apply.Id });
                await _mediator.Publish(new RunUpdated(apply.RunId));
                return _mapper.Map<Apply>(apply);
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
