// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.Capacity;
using Caster.Api.Features.Shared;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using MediatR;

namespace Caster.Api.Features.Capacity;

public class GetRunCapacity
{
    public class Query : IRequest<CapacityCheck>
    {
        public Guid RunId { get; set; }
    }

    public class Handler(ICasterAuthorizationService authorizationService, ICapacityCheckService capacity) : BaseHandler<Query, CapacityCheck>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<Run>(
                request.RunId,
                [SystemPermission.ViewProjects],
                [ProjectPermission.ViewProject],
                cancellationToken);

        public override async Task<CapacityCheck> HandleRequest(Query request, CancellationToken cancellationToken) =>
            await capacity.CheckRunAsync(request.RunId, cancellationToken)
                ?? throw new EntityNotFoundException<Domain.Models.Plan>("The run has no plan to check.");
    }
}
