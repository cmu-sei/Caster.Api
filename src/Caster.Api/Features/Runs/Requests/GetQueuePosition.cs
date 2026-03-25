// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using MediatR;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Shared;

namespace Caster.Api.Features.Runs;

public class GetQueuePosition
{
    public class Query : IRequest<QueuePosition>
    {
        public Guid RunId { get; set; }
    }

    public class Handler(
        ICasterAuthorizationService authorizationService,
        IRunQueueService runQueueService,
        IMapper mapper) : BaseHandler<Query, QueuePosition>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<Domain.Models.Run>(
                request.RunId,
                [SystemPermission.ViewProjects, SystemPermission.ViewWorkspaces],
                [ProjectPermission.ViewProject],
                cancellationToken);

        public override Task<QueuePosition> HandleRequest(Query request, CancellationToken cancellationToken)
            => Task.FromResult(mapper.Map<QueuePosition>(runQueueService.GetQueuePosition(request.RunId)));
    }
}
