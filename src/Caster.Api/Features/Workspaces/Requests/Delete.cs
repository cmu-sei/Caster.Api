// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Domain.Events;
using Caster.Api.Features.Workspaces.Interfaces;
using Caster.Api.Domain.Services;

namespace Caster.Api.Features.Workspaces
{
    public class Delete
    {
        [DataContract(Name="DeleteWorkspaceCommand")]
        public class Command : IRequest, IWorkspaceDeleteRequest
        {
            public Guid Id { get; set; }
        }

        public class Handler : AsyncRequestHandler<Command>
        {
            private readonly CasterContext _db;
            private readonly IMapper _mapper;
            private readonly IAuthorizationService _authorizationService;
            private readonly ClaimsPrincipal _user;
            private readonly IMediator _mediator;
            private readonly ILockService _lockService;

            public Handler(
                CasterContext db,
                IMapper mapper,
                IAuthorizationService authorizationService,
                IIdentityResolver identityResolver,
                ILockService lockService)
            {
                _db = db;
                _mapper = mapper;
                _authorizationService = authorizationService;
                _user = identityResolver.GetClaimsPrincipal();
                _lockService = lockService;
            }

            protected override async Task Handle(Command request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                var workspace = await _db.Workspaces.FindAsync(request.Id);

                if (workspace == null)
                    throw new EntityNotFoundException<Workspace>();

                using (var lockResult = await _lockService.GetWorkspaceLock(request.Id).LockAsync(0))
                {
                    if (!lockResult.AcquiredLock)
                        throw new WorkspaceConflictException();

                    if (workspace.GetState().GetResources().Any())
                        throw new ConflictException("Cannot delete a Workspace with deployed Resources.");

                    if (await _db.AnyIncompleteRuns(request.Id))
                        throw new ConflictException("Cannot delete a Workspace with pending Runs.");

                    _db.Workspaces.Remove(workspace);
                    await _db.SaveChangesAsync(cancellationToken);
                }
            }
        }
    }
}
