// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Shared;
using System.Linq;
using System;

namespace Caster.Api.Features.GroupPermissions
{
    public class GetMine
    {
        [DataContract(Name = "GetMyGroupPermissionsQuery")]
        public record Query : IRequest<GroupPermissionsClaim[]>
        {
            [DataMember]
            public Guid? GroupId { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService) : BaseHandler<Query, GroupPermissionsClaim[]>
        {
            public override Task<bool> Authorize(Query request, CancellationToken cancellationToken) => Task.FromResult(true);

            public override Task<GroupPermissionsClaim[]> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                return Task.FromResult(authorizationService.GetGroupPermissions(request.GroupId).ToArray());
            }
        }
    }
}
