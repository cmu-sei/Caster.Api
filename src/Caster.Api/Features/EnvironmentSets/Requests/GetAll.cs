// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Shared;
using Caster.Api.Infrastructure.Authorization;
using MediatR;

namespace Caster.Api.Features.EnvironmentSets;

public class GetAll
{
    [DataContract(Name = "GetEnvironmentSetsQuery")]
    public class Query : IRequest<EnvironmentSet[]>
    {
    }

    public class Handler(ICasterAuthorizationService authorizationService, IEnvironmentSetViews views) : BaseHandler<Query, EnvironmentSet[]>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ViewEnvironmentSets], cancellationToken);

        public override async Task<EnvironmentSet[]> HandleRequest(Query request, CancellationToken cancellationToken) =>
            await views.GetSets(null, cancellationToken);
    }
}
