// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using MediatR;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Shared;

namespace Caster.Api.Features.Runs;

public class GetQueuePositions
{
    public class Query : IRequest<IEnumerable<QueuePosition>> { }

    public class Handler(
        ICasterAuthorizationService authorizationService,
        IRunQueueService runQueueService,
        IMapper mapper) : BaseHandler<Query, IEnumerable<QueuePosition>>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ViewWorkspaces], cancellationToken);

        public override Task<IEnumerable<QueuePosition>> HandleRequest(Query request, CancellationToken cancellationToken)
            => Task.FromResult(mapper.Map<IEnumerable<QueuePosition>>(runQueueService.GetQueuePositions()));
    }
}
