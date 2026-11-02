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
using Caster.Api.Domain.Services.Topology;
using Caster.Api.Features.Shared;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Options;
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

        public class Handler(
            ICasterAuthorizationService authorizationService,
            CasterContext dbContext,
            TerraformOptions terraformOptions) : BaseHandler<Query, WorkspaceTopology>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Domain.Models.Workspace>(request.Id, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], cancellationToken);

            public override async Task<WorkspaceTopology> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                var workspace = await dbContext.Workspaces
                    .Where(x => x.Id == request.Id)
                    .Select(x => new { x.TopologyLayout, x.DirectoryId })
                    .FirstOrDefaultAsync(cancellationToken)
                    ?? throw new EntityNotFoundException<Workspace>();

                var latest = await dbContext.Plans
                    .Where(x => x.Run.WorkspaceId == request.Id && x.Topology != null)
                    .OrderByDescending(x => x.Run.CreatedAt)
                    .Select(x => new { x.RunId, x.Run.CreatedAt, x.Run.IsDestroy, x.Run.Status, x.Topology })
                    .FirstOrDefaultAsync(cancellationToken);

                // No plan with a topology and nothing in the state is an empty
                // graph, not an error, so the view can say "run a plan" without
                // special casing a 404.
                var result = new WorkspaceTopology { WorkspaceId = request.Id };
                var plan = latest == null
                    ? null
                    : JsonSerializer.Deserialize<Topology>(latest.Topology, DefaultJsonSettings.Settings);

                if (latest != null)
                {
                    result.RunId = latest.RunId;
                    result.CreatedAt = latest.CreatedAt;
                    result.IsDestroy = latest.IsDestroy;
                }

                // Once the plan is applied, failed or rejected, what it would do
                // no longer describes anything; the state says what is there. A
                // state error means the stored state is stale, so the plan is
                // the better guess.
                var topology = plan;

                if (latest == null || latest.Status is RunStatus.Applied or RunStatus.Failed or RunStatus.Rejected)
                {
                    var state = await dbContext.Workspaces
                        .Where(x => x.Id == request.Id)
                        .Select(x => x.State)
                        .FirstOrDefaultAsync(cancellationToken);

                    topology = AppliedTopology.Read(state, plan, latest?.Status == RunStatus.Failed, terraformOptions.Topology);
                    result.Source = TopologySource.State;
                }

                // A plan stored before teams were named, and a state, name
                // teams from the variables the workspace's runs are given.
                if (topology.Nodes.Any(x => x.Console?.Teams.Any(t => t.Name == null) == true))
                {
                    var tfvars = await dbContext.Files
                        .Where(x => !x.IsDeleted && x.Name.EndsWith(".tfvars") &&
                            (x.WorkspaceId == request.Id || (x.WorkspaceId == null && x.DirectoryId == workspace.DirectoryId)))
                        .OrderBy(x => x.WorkspaceId == null)
                        .Select(x => x.Content)
                        .ToListAsync(cancellationToken);

                    TopologyTeamNames.Fill(topology, TopologyTeamNames.FromTfvars(tfvars));
                }

                result.Nodes = topology.Nodes;
                result.Edges = topology.Edges;

                // A node the topology no longer draws keeps its saved layout, so
                // it is back where it was if a later plan draws it again.
                var ids = topology.Nodes.Select(x => x.Id).ToHashSet();
                result.Layout = EditTopologyLayout.Read(workspace.TopologyLayout)
                    .Where(x => ids.Contains(x.Key))
                    .ToDictionary(x => x.Key, x => x.Value);

                return result;
            }
        }
    }
}
