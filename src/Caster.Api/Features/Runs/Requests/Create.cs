// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using AutoMapper;
using System.Runtime.Serialization;
using System.Linq;
using Caster.Api.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Domain.Events;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Domain.Services;
using Caster.Api.Infrastructure.Identity;
using AutoMapper.QueryableExtensions;
using Caster.Api.Infrastructure.Extensions;

namespace Caster.Api.Features.Runs
{
    public class Create
    {
        [DataContract(Name = "CreateRunCommand")]
        public class Command : IRequest<Run>
        {
            /// <summary>
            /// The Id of the Workspace to create the Run in
            /// </summary>
            [DataMember]
            public Guid WorkspaceId { get; set; }

            /// <summary>
            /// If true, will create a Run to destroy all resources in the Workspace
            /// </summary>
            [DataMember]
            public bool IsDestroy { get; set; }

            /// <summary>
            /// Optional list of resources to constrain the affects of this Run to
            /// </summary>
            [DataMember]
            public string[] Targets { get; set; }
        }

        public class Handler : IRequestHandler<Command, Run>
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

            public async Task<Run> Handle(Command request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                await ValidateWorkspace(request.WorkspaceId);

                Domain.Models.Run run = null;

                using (var lockResult = await _lockService.GetWorkspaceLock(request.WorkspaceId).LockAsync(0))
                {
                    if (!lockResult.AcquiredLock)
                        throw new WorkspaceConflictException();

                    if ((await _db.AnyIncompleteRuns(request.WorkspaceId)))
                    {
                        throw new ConflictException("This Workspace's current Run must be rejected or applied before a new one can be created.");
                    }

                    run = await this.DoWork(request);
                }

                await _mediator.Publish(new RunCreated { RunId = run.Id });

                return await _db.Runs
                    .ProjectTo<Run>(_mapper.ConfigurationProvider)
                    .SingleOrDefaultAsync(x => x.Id == run.Id, cancellationToken);
            }

            private async Task<Domain.Models.Run> DoWork(Command request)
            {
                var run = _mapper.Map<Domain.Models.Run>(request);
                run.CreatedById = _user.GetId();
                run.Modify(_user.GetId());
                await _db.Runs.AddAsync(run);
                await _db.SaveChangesAsync();
                return run;
            }

            private async Task ValidateWorkspace(Guid workspaceId)
            {
                var workspace = await _db.Workspaces.FindAsync(workspaceId);

                if (workspace == null)
                    throw new EntityNotFoundException<Workspace>();
            }
        }
    }
}
