// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Workspaces
{
    /// <summary>
    /// The machines and networks of a Workspace's most recent plan that has a
    /// topology, and which network each NIC is attached to.
    /// </summary>
    public class WorkspaceTopology
    {
        public Guid WorkspaceId { get; set; }

        /// <summary>The Run whose plan this was read from. Null when no plan has a topology yet.</summary>
        public Guid? RunId { get; set; }

        public DateTime? CreatedAt { get; set; }

        /// <summary>True when that plan was a destroy, so every node's Action is delete.</summary>
        public bool IsDestroy { get; set; }

        public List<TopologyNode> Nodes { get; set; } = [];
        public List<TopologyEdge> Edges { get; set; } = [];
    }
}
