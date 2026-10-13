// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Shared;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.Workspaces
{
    public class GetTopology
    {
        [DataContract(Name = "GetWorkspaceTopologyQuery")]
        public class Query : IRequest<WorkspaceTopology>
        {
            /// <summary>
            /// The Id of the Workspace
            /// </summary>
            [DataMember]
            public Guid Id { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext) : BaseHandler<Query, WorkspaceTopology>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Domain.Models.Workspace>(request.Id, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], cancellationToken);

            public override async Task<WorkspaceTopology> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                if (!await dbContext.Workspaces.AnyAsync(x => x.Id == request.Id, cancellationToken))
                    throw new EntityNotFoundException<Workspace>();

                var latest = await dbContext.Plans
                    .Where(x => x.Run.WorkspaceId == request.Id && x.Topology != null)
                    .OrderByDescending(x => x.Run.CreatedAt)
                    .Select(x => new { x.RunId, x.Run.CreatedAt, x.Run.IsDestroy, x.Topology })
                    .FirstOrDefaultAsync(cancellationToken);

                // No plan with a topology yet is an empty graph, not an error,
                // so the view can say "run a plan" without special casing a 404.
                var result = new WorkspaceTopology { WorkspaceId = request.Id };

                if (latest != null)
                {
                    var topology = JsonSerializer.Deserialize<Topology>(latest.Topology, DefaultJsonSettings.Settings);

                    result.RunId = latest.RunId;
                    result.CreatedAt = latest.CreatedAt;
                    result.IsDestroy = latest.IsDestroy;
                    result.Nodes = topology.Nodes;
                    result.Edges = topology.Edges;
                }

                return result;
            }
        }
    }
}
