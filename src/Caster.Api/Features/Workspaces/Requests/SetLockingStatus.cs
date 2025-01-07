// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Domain.Services;
using Caster.Api.Domain.Events;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Workspaces
{
    public class SetLockingStatus
    {
        [DataContract(Name = "SetWorkspaceLockingStatusQuery")]
        public class Command : IRequest<bool>
        {
            public bool Enabled { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, ILockService lockService, IMediator mediator) : BaseHandler<Command, bool>
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ManageWorkspaces], cancellationToken);

            public override async Task<bool> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                if (request.Enabled)
                {
                    lockService.EnableWorkspaceLocking();
                }
                else
                {
                    lockService.DisableWorkspaceLocking();
                }

                var lockingEnabled = lockService.IsWorkspaceLockingEnabled();
                await mediator.Publish(new WorkspaceSettingsUpdated(lockingEnabled));

                return lockingEnabled;
            }
        }
    }
}
