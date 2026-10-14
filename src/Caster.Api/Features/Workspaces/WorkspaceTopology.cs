// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Workspaces
{
    /// <summary>
    /// A Workspace's machines and networks, and which network each NIC is
    /// attached to: from its latest plan while that plan is still to be
    /// applied, otherwise from its state.
    /// </summary>
    public class WorkspaceTopology
    {
        public Guid WorkspaceId { get; set; }

        /// <summary>
        /// Plan while the latest plan's Run is still to be applied; State once
        /// it has been applied, has failed or was rejected.
        /// </summary>
        public TopologySource Source { get; set; }

        /// <summary>The Run of the latest plan with a topology. Null when there is none yet.</summary>
        public Guid? RunId { get; set; }

        public DateTime? CreatedAt { get; set; }

        /// <summary>True when that plan was a destroy.</summary>
        public bool IsDestroy { get; set; }

        public List<TopologyNode> Nodes { get; set; } = [];
        public List<TopologyEdge> Edges { get; set; } = [];
    }
}
