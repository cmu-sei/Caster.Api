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

namespace Caster.Api.Features.ProjectPermissions
{
    public class GetMine
    {
        [DataContract(Name = "GetMyProjectPermissionsQuery")]
        public record Query : IRequest<ProjectPermissionsClaim[]>
        {
            [DataMember]
            public Guid? ProjectId { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService) : BaseHandler<Query, ProjectPermissionsClaim[]>
        {
            public override Task Authorize(Query request, CancellationToken cancellationToken) => Task.CompletedTask;

            public override Task<ProjectPermissionsClaim[]> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                return Task.FromResult(authorizationService.GetProjectPermissions(request.ProjectId).ToArray());
            }
        }
    }
}

