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
using Caster.Api.Infrastructure.Authorization;
using System.Linq;
using System.Text.Json.Serialization;
using System.Web;
using Caster.Api.Features.Shared;

namespace Caster.Api.Features.Resources
{
    public class Get
    {
        [DataContract(Name = "GetResourceQuery")]
        public class Query : IRequest<Resource>
        {
            /// <summary>
            /// Id of the Workspace that the Resource exists in.
            /// </summary>
            [JsonIgnore]
            public Guid WorkspaceId { get; set; }

            /// <summary>
            /// Address of the Resource
            /// </summary>
            [JsonIgnore]
            public string Address { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, Resource>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Workspace>(request.WorkspaceId, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], cancellationToken);

            public override async Task<Resource> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                var workspace = await dbContext.Workspaces.FindAsync([request.WorkspaceId], cancellationToken);

                if (workspace == null)
                    throw new EntityNotFoundException<Workspace>();

                var state = workspace.GetState();
                var resources = state.GetResources();

                var address = HttpUtility.UrlDecode(request.Address);

                var resource = resources.Where(r => r.Address == address).FirstOrDefault();
                return mapper.Map<Resource>(resource, opts => opts.ExcludeMembers());
            }
        }
    }
}
