// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;

namespace Caster.Api.Domain.Models;

/// <summary>
/// The machines and networks a plan describes, and which network each NIC is
/// attached to. Built from <c>terraform show -json</c> of the plan, not from
/// <c>terraform graph</c>: the graph shows creation order, and drops the
/// machine to network edge as soon as a machine also depends_on the network.
/// </summary>
public class Topology
{
    public List<TopologyNode> Nodes { get; set; } = [];
    public List<TopologyEdge> Edges { get; set; } = [];
}

public class TopologyNode
{
    /// <summary>
    /// The resource instance address, such as <c>module.host["dc"].proxmox_virtual_environment_vm.vm</c>.
    /// A network no resource in the plan creates (a NIC on <c>vmbr0</c>) gets
    /// <c>external:&lt;value&gt;</c>.
    /// </summary>
    public string Id { get; set; }

    public TopologyNodeKind Kind { get; set; }

    /// <summary>Resource type, such as <c>proxmox_sdn_vnet</c>. Null for an external network.</summary>
    public string Type { get; set; }

    /// <summary>What a person calls it: the VM name, the vnet alias, the port group name.</summary>
    public string Label { get; set; }

    /// <summary>Module instance address, such as <c>module.host["dc"]</c>. Null at the root.</summary>
    public string Module { get; set; }

    /// <summary>
    /// What the plan does to it: create, update, replace, delete, read or no-op.
    /// Null for an external network, and for anything read from the state.
    /// </summary>
    public string Action { get; set; }

    /// <summary>True for a network that is referenced by value but not managed in this workspace.</summary>
    public bool External { get; set; }

    /// <summary>
    /// True when the plan creates it but the apply failed before it did: it is
    /// in the plan and not in the state.
    /// </summary>
    public bool NotCreated { get; set; }

    /// <summary>A few display values, such as cpus and memory_mb, when the plan knows them.</summary>
    public Dictionary<string, string> Properties { get; set; } = [];
}

public enum TopologyNodeKind
{
    Machine,
    Network,
}

/// <summary>
/// Where someone put a node in the topology view, and what it is for. Saved on
/// the workspace by node id, so everyone who opens the view sees the same
/// arrangement, and it outlives the runs that drew the node.
/// </summary>
public class TopologyNodeLayout
{
    /// <summary>The node's position in the force layout. Null until someone moves it there, so the layout places it.</summary>
    public double? X { get; set; }
    public double? Y { get; set; }

    /// <summary>The node's position in the tree layout, kept apart from the force layout's so each keeps its own arrangement.</summary>
    public double? TreeX { get; set; }
    public double? TreeY { get; set; }

    /// <summary>What the node is for, which picks its icon. Null shows the icon for its kind.</summary>
    public TopologyNodeRole? Role { get; set; }
}

/// <summary>What a machine is for in the exercise, shown as its icon.</summary>
public enum TopologyNodeRole
{
    Server,
    Workstation,
    DomainController,
    Firewall,
    Router,
    WebServer,
    Database,
    MailServer,
    Linux,
    Windows,
    Attacker,
}

/// <summary>Where a workspace's topology was read from.</summary>
public enum TopologySource
{
    /// <summary>The latest plan, still to be applied: what applying it will do.</summary>
    Plan,

    /// <summary>The workspace's state: what exists now.</summary>
    State,
}

/// <summary>One NIC: machine <see cref="From"/>, NIC <see cref="Nic"/>, attached to network <see cref="To"/>.</summary>
public class TopologyEdge
{
    public string From { get; set; }
    public string To { get; set; }
    /// <summary>The NIC's position on the machine, from 0.</summary>
    public int Nic { get; set; }
}
