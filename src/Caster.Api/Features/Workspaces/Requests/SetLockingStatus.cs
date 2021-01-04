// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Domain.Services;
using Caster.Api.Domain.Events;

namespace Caster.Api.Features.Workspaces
{
    public class SetLockingStatus
    {
        [DataContract(Name="SetWorkspaceLockingStatusQuery")]
        public class Command : IRequest<bool>
        {
            public bool Enabled { get; set; }
        }

        public class Handler : IRequestHandler<Command, bool>
        {
            private readonly IAuthorizationService _authorizationService;
            private readonly ClaimsPrincipal _user;
            private readonly ILockService _lockService;
            private readonly IMediator _mediator;

            public Handler(
                IAuthorizationService authorizationService,
                IIdentityResolver identityResolver,
                ILockService lockService,
                IMediator mediator)
            {
                _authorizationService = authorizationService;
                _user = identityResolver.GetClaimsPrincipal();
                _lockService = lockService;
                _mediator = mediator;
            }

            public async Task<bool> Handle(Command request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded)
                    throw new ForbiddenException();

                if (request.Enabled)
                {
                    _lockService.EnableWorkspaceLocking();
                }
                else
                {
                    _lockService.DisableWorkspaceLocking();
                }

                var lockingEnabled = _lockService.IsWorkspaceLockingEnabled();
                await _mediator.Publish(new WorkspaceSettingsUpdated(lockingEnabled));

                return lockingEnabled;
            }
        }
    }
}
