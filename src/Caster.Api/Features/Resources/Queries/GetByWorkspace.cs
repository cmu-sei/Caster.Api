// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using System.Runtime.Serialization;
using Caster.Api.Data;
using System;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Exceptions;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;
using System.Text.Json.Serialization;
using Caster.Api.Features.Shared;

namespace Caster.Api.Features.Resources
{
    public class GetByWorkspace
    {
        [DataContract(Name = "GetResourcesByWorkspaceQuery")]
        public class Query : IRequest<Resource[]>
        {
            /// <summary>
            /// ID of the Workspace.
            /// </summary>
            [JsonIgnore]
            public Guid WorkspaceId { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, Resource[]>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Workspace>(request.WorkspaceId, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], cancellationToken);

            public override async Task<Resource[]> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                var workspace = await dbContext.Workspaces.FindAsync(request.WorkspaceId);

                if (workspace == null)
                {
                    throw new EntityNotFoundException<Workspace>();
                }

                var state = workspace.GetState();
                return mapper.Map<Resource[]>(state.GetResources(), opts => opts.ExcludeMembers(nameof(Resource.Attributes)));
            }
        }
    }
}
