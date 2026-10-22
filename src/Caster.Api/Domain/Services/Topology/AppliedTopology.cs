// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Options;

namespace Caster.Api.Domain.Services.Topology;

/// <summary>What a workspace's machines and networks are once its plan is no longer pending.</summary>
public static class AppliedTopology
{
    /// <summary>
    /// The workspace's state, and, after a failed apply, what the plan would
    /// have created that the state does not have, marked
    /// <see cref="TopologyNode.NotCreated"/>.
    /// </summary>
    /// <remarks>
    /// Caster retrieves the state after a failed apply too, so the state holds
    /// whatever was built before the failure. Something the plan changed or
    /// destroyed is still in the state if the apply did not get to it, and is
    /// drawn as it is.
    /// </remarks>
    public static Models.Topology Read(string state, Models.Topology plan, bool applyFailed, TopologyOptions options = null)
    {
        var topology = string.IsNullOrEmpty(state) ? new Models.Topology() : PlanTopologyParser.ParseState(state, options);

        if (!applyFailed || plan == null)
        {
            return topology;
        }

        var ids = topology.Nodes.Select(x => x.Id).ToHashSet();
        var planned = plan.Nodes.ToDictionary(x => x.Id);
        var notCreated = plan.Nodes
            .Where(x => !x.External && (x.Action == "create" || x.Action == "replace") && !ids.Contains(x.Id))
            .Select(x => x.Id)
            .ToHashSet();

        foreach (var id in notCreated)
        {
            Add(topology, ids, planned[id]);
        }

        foreach (var edge in plan.Edges.Where(x => notCreated.Contains(x.From)))
        {
            if (!ids.Contains(edge.To) && planned.TryGetValue(edge.To, out var network))
            {
                Add(topology, ids, network);
            }

            topology.Edges.Add(edge);
        }

        topology.Nodes = [.. topology.Nodes.OrderBy(x => x.Kind).ThenBy(x => x.Id, System.StringComparer.Ordinal)];

        return topology;
    }

    private static void Add(Models.Topology topology, HashSet<string> ids, TopologyNode node)
    {
        topology.Nodes.Add(new TopologyNode
        {
            Id = node.Id,
            Kind = node.Kind,
            Type = node.Type,
            Label = node.Label,
            Module = node.Module,
            External = node.External,
            NotCreated = !node.External,
            Properties = node.Properties,
        });
        ids.Add(node.Id);
    }
}
