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

    /// <summary>What the plan does to it: create, update, replace, delete, read or no-op. Null for an external network.</summary>
    public string Action { get; set; }

    /// <summary>True for a network that is referenced by value but not managed in this workspace.</summary>
    public bool External { get; set; }

    /// <summary>A few display values, such as cpus and memory_mb, when the plan knows them.</summary>
    public Dictionary<string, string> Properties { get; set; } = [];
}

public enum TopologyNodeKind
{
    Machine,
    Network,
}

/// <summary>One NIC: machine <see cref="From"/>, NIC <see cref="Nic"/>, attached to network <see cref="To"/>.</summary>
public class TopologyEdge
{
    public string From { get; set; }
    public string To { get; set; }
    /// <summary>The NIC's position on the machine, from 0.</summary>
    public int Nic { get; set; }
}
