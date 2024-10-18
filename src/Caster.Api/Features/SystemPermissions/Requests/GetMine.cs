// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Shared;
using System.Linq;

namespace Caster.Api.Features.SystemPermissions
{
    public class GetMine
    {
        [DataContract(Name = "GetMySystemPermissionsQuery")]
        public class Query : IRequest<Domain.Models.SystemPermission[]>
        {
        }

        public class Handler(ICasterAuthorizationService authorizationService) : BaseHandler<Query, Domain.Models.SystemPermission[]>
        {
            public override Task<bool> Authorize(Query request, CancellationToken cancellationToken) => Task.FromResult(true);

            public override Task<Domain.Models.SystemPermission[]> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                return Task.FromResult(authorizationService.GetSystemPermissions().ToArray());
            }
        }
    }
}

