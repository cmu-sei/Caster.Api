// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Domain.Services;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Workspaces
{
    public class GetLockingStatus
    {
        [DataContract(Name = "GetWorkspaceLockingStatusQuery")]
        public class Query : IRequest<bool> { }

        public class Handler(ICasterAuthorizationService authorizationService, ILockService lockService) : BaseHandler<Query, bool>
        {
            public override async Task Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ViewWorkspaces], cancellationToken);

            public override Task<bool> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                return Task.FromResult(lockService.IsWorkspaceLockingEnabled());
            }
        }
    }
}
