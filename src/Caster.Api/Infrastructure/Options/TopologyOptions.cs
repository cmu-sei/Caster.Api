// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;

namespace Caster.Api.Infrastructure.Options;

/// <summary>
/// Resource types the Topology view draws, in addition to the built-in ones
/// (Proxmox, vSphere, AWS and Azure). An entry with a built-in type's name replaces it.
/// </summary>
/// <remarks>
/// Every attribute is a path into the resource's values, with list indexes as
/// numbers: <c>cpu.0.cores</c>, <c>tags.Name</c>.
/// </remarks>
public class TopologyOptions
{
    public Dictionary<string, TopologyMachineType> Machines { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, TopologyNetworkType> Networks { get; set; } = new(StringComparer.Ordinal);

    /// <summary>NICs that are resources of their own, which a machine lists by id: <c>azurerm_network_interface</c>.</summary>
    public Dictionary<string, TopologyInterfaceType> Interfaces { get; set; } = new(StringComparer.Ordinal);
}

public class TopologyMachineType
{
    /// <summary>
    /// The attribute holding the NICs: a list (<c>network_device</c>), or a map
    /// keyed by slot (<c>net0</c>, <c>net1</c>). Empty when the machine names
    /// its one network itself, as <c>aws_instance.subnet_id</c> does.
    /// </summary>
    public string Nics { get; set; }

    /// <summary>
    /// The NIC's attribute naming its network, matched against each network's
    /// <see cref="TopologyNetworkType.Keys"/>. Empty for a machine with no NICs
    /// to read: it is drawn unconnected.
    /// </summary>
    public string Network { get; set; }

    /// <summary>The NIC's VLAN tag, if it has one.</summary>
    public string Vlan { get; set; }

    /// <summary>
    /// The attribute listing the machine's <see cref="TopologyOptions.Interfaces"/>
    /// by id, after any NICs in <see cref="Nics"/>: <c>network_interface_ids</c>.
    /// </summary>
    public string Interfaces { get; set; }

    /// <summary>The first of these that has a value labels the node; <c>name</c> when not set.</summary>
    public string[] Label { get; set; }

    /// <summary>Shown with the node: property name to attribute, such as <c>cpus</c> = <c>cpu.0.cores</c>.</summary>
    public Dictionary<string, string> Properties { get; set; } = new(StringComparer.Ordinal);
}

public class TopologyNetworkType
{
    /// <summary>The attributes a NIC's network value can match: <c>id</c>, <c>name</c>.</summary>
    public string[] Keys { get; set; }

    /// <summary>The first of these that has a value labels the node; <c>name</c> when not set.</summary>
    public string[] Label { get; set; }

    /// <summary>The network's VLAN tag, if it has one.</summary>
    public string Vlan { get; set; }
}

public class TopologyInterfaceType
{
    /// <summary>The attributes a machine's interface id can match: <c>id</c>.</summary>
    public string[] Keys { get; set; }

    /// <summary>The attribute naming its network, matched against each network's <see cref="TopologyNetworkType.Keys"/>: <c>ip_configuration.0.subnet_id</c>.</summary>
    public string Network { get; set; }
}
