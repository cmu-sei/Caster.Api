// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Shared;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using MediatR;

namespace Caster.Api.Features.ModuleScopes;

public class Get
{
    [DataContract(Name = "GetModuleScopeQuery")]
    public class Query : IRequest<ModuleScope>
    {
        public Guid Id { get; set; }
    }

    public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext) : BaseHandler<Query, ModuleScope>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ViewModules], cancellationToken);

        public override async Task<ModuleScope> HandleRequest(Query request, CancellationToken cancellationToken) =>
            await dbContext.GetScope(request.Id, cancellationToken)
                ?? throw new EntityNotFoundException<Domain.Models.ModuleScope>();
    }
}
