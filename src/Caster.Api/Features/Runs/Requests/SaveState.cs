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
using Caster.Api.Infrastructure.Options;
using AutoMapper.QueryableExtensions;

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

        public class Handler : IRequestHandler<Command, Run>
        {
            private readonly CasterContext _db;
            private readonly IMapper _mapper;
            private readonly IMediator _mediator;
            private readonly IAuthorizationService _authorizationService;
            private readonly ClaimsPrincipal _user;
            private readonly ILockService _lockService;
            private readonly TerraformOptions _options;

            public Handler(
                CasterContext db,
                IMapper mapper,
                IMediator mediator,
                IAuthorizationService authorizationService,
                IIdentityResolver identityResolver,
                ILockService lockService,
                TerraformOptions options)
            {
                _db = db;
                _mapper = mapper;
                _mediator = mediator;
                _authorizationService = authorizationService;
                _user = identityResolver.GetClaimsPrincipal();
                _lockService = lockService;
                _options = options;
            }

            public async Task<Run> Handle(Command request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                var run = await _db.Runs
                    .Include(r => r.Apply)
                    .Include(r => r.Workspace)
                    .Where(r => r.Id == request.RunId)
                    .FirstOrDefaultAsync(cancellationToken);

                if (run == null)
                    throw new EntityNotFoundException<Run>();

                using (var lockResult = await _lockService.GetWorkspaceLock(run.WorkspaceId).LockAsync(0))
                {
                    if (!lockResult.AcquiredLock)
                        throw new WorkspaceConflictException();

                    await ValidateRun(run, cancellationToken);

                    var workingDir = run.Workspace.GetPath(_options.RootWorkingDirectory);
                    var stateRetrieved = await run.Workspace.RetrieveState(workingDir);

                    if (stateRetrieved)
                    {
                        run.Apply.Status = run.Apply.Status == ApplyStatus.Applied_StateError ? ApplyStatus.Applied : ApplyStatus.Failed;
                        run.Status = run.Status == RunStatus.Applied_StateError ? RunStatus.Applied : RunStatus.Failed;

                        await _db.SaveChangesAsync(cancellationToken);
                        await _mediator.Publish(new RunUpdated(run.Id));
                        await _mediator.Publish(new ApplyCompleted(run.Workspace));
                        run.Workspace.CleanupFileSystem(_options.RootWorkingDirectory);
                    }
                }

                return await _db.Runs
                    .ProjectTo<Run>(_mapper.ConfigurationProvider)
                    .SingleOrDefaultAsync(x => x.Id == run.Id, cancellationToken);
            }

            private async Task ValidateRun(Domain.Models.Run run, CancellationToken cancellationToken)
            {
                if (run.Status != RunStatus.Applied_StateError && run.Status != RunStatus.Failed_StateError)
                    throw new WorkspaceConflictException();

                var notLatest = await _db.Runs
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
