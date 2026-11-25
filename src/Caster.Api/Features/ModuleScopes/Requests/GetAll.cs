// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Shared;
using Caster.Api.Infrastructure.Authorization;
using MediatR;

namespace Caster.Api.Features.ModuleScopes;

/// <summary>Read with ViewModules, or with ManageModules, so whoever can change the scopes can see them.</summary>
public class GetAll
{
    [DataContract(Name = "GetModuleScopesQuery")]
    public class Query : IRequest<ModuleScope[]>
    {
    }

    public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext) : BaseHandler<Query, ModuleScope[]>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ViewModules, SystemPermission.ManageModules], cancellationToken);

        public override async Task<ModuleScope[]> HandleRequest(Query request, CancellationToken cancellationToken) =>
            await dbContext.GetScopes(null, cancellationToken);
    }
}
