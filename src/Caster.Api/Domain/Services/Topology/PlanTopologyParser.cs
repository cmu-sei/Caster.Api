// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Options;

namespace Caster.Api.Domain.Services.Topology;

/// <summary>
/// Reads the machines, networks and NIC attachments out of
/// <c>terraform show -json &lt;plan&gt;</c>, or out of a workspace's state file.
/// </summary>
/// <remarks>
/// A NIC is matched to a network by value first: the planned
/// <c>bridge</c>/<c>network_id</c> against each network's id. That works
/// through any number of module boundaries and for_each maps, which the
/// configuration's <c>references</c> cannot see past. When the value is not
/// known until apply (a vSphere port group created in the same plan), the
/// NIC's own <c>references</c> are tried instead, within the same module.
/// Only the resource types in <see cref="Types"/>, and any added in
/// <see cref="TopologyOptions"/>, are drawn; everything else in the plan is ignored.
///
/// A NIC that is a resource of its own (<c>azurerm_network_interface</c>) is
/// followed to its network the same way, value first and then references,
/// and is not drawn: the machine is wired straight to the network.
///
/// A Proxmox NIC with a VLAN tag on a plain bridge is on a different segment
/// from an untagged NIC on that bridge, so the tag is part of its key:
/// <c>vmbr0.10</c>, the way Linux names the VLAN interface.
/// </remarks>
public static partial class PlanTopologyParser
{
    private static TopologyMachineType Machine(
        string nics, string network, string vlan, string[] label = null, Dictionary<string, string> properties = null,
        string interfaces = null) =>
        new() { Nics = nics, Network = network, Vlan = vlan, Interfaces = interfaces, Label = label, Properties = properties ?? [] };

    private static TopologyNetworkType Network(string[] keys, string[] label = null, string vlan = null) =>
        new() { Keys = keys, Label = label, Vlan = vlan };

    // bpg/proxmox names each resource twice: proxmox_virtual_environment_vm
    // and, since 0.80, a short alias. The aliases are listed under both names.
    private static readonly Dictionary<string, TopologyMachineType> BuiltInMachines = new(StringComparer.Ordinal)
    {
        ["proxmox_virtual_environment_vm"] = Machine(
            "network_device", "bridge", "vlan_id", properties: new()
            {
                ["cpus"] = "cpu.0.cores", ["memory_mb"] = "memory.0.dedicated", ["node"] = "node_name",
            }),
        // network is a map keyed by slot: net0, net1.
        ["proxmox_virtual_environment_cloned_vm"] = Machine(
            "network", "bridge", "tag", properties: new()
            {
                ["cpus"] = "cpu.cores", ["memory_mb"] = "memory.size", ["node"] = "node_name",
            }),
        // Experimental in bpg, with no NICs yet: drawn unconnected.
        ["proxmox_virtual_environment_vm2"] = Machine(
            null, null, null, properties: new() { ["cpus"] = "cpu.cores", ["node"] = "node_name" }),
        ["proxmox_virtual_environment_container"] = Machine(
            "network_interface", "bridge", "vlan_id", ["initialization.0.hostname"], new()
            {
                ["cpus"] = "cpu.0.cores", ["memory_mb"] = "memory.0.dedicated", ["node"] = "node_name",
            }),
        ["proxmox_vm_qemu"] = Machine(
            "network", "bridge", "tag", properties: new()
            {
                ["cpus"] = "cores", ["memory_mb"] = "memory", ["node"] = "target_node",
            }),
        ["proxmox_lxc"] = Machine(
            "network", "bridge", "tag", ["hostname"], new()
            {
                ["cpus"] = "cores", ["memory_mb"] = "memory", ["node"] = "target_node",
            }),
        ["vsphere_virtual_machine"] = Machine(
            "network_interface", "network_id", null, properties: new()
            {
                ["cpus"] = "num_cpus", ["memory_mb"] = "memory",
            }),
        // subnet_id is the primary NIC; more are aws_network_interface resources
        // attached from their side, which this does not follow.
        ["aws_instance"] = Machine(
            null, "subnet_id", null, ["tags.Name"], new() { ["instance_type"] = "instance_type" }),
        ["azurerm_linux_virtual_machine"] = Machine(
            null, null, null, properties: new() { ["size"] = "size", ["location"] = "location" },
            interfaces: "network_interface_ids"),
        ["azurerm_windows_virtual_machine"] = Machine(
            null, null, null, properties: new() { ["size"] = "size", ["location"] = "location" },
            interfaces: "network_interface_ids"),
        // The original azurerm VM resource, still in older ranges.
        ["azurerm_virtual_machine"] = Machine(
            null, null, null, properties: new() { ["size"] = "vm_size", ["location"] = "location" },
            interfaces: "network_interface_ids"),
    };

    private static readonly Dictionary<string, TopologyNetworkType> BuiltInNetworks = new(StringComparer.Ordinal)
    {
        // A NIC's bridge is the vnet id; the alias is the readable name.
        ["proxmox_virtual_environment_sdn_vnet"] = Network(["id"], ["alias", "id"], "tag"),
        ["proxmox_virtual_environment_network_linux_bridge"] = Network(["name"]),
        // network_id is the managed object id: dvportgroup-123, network-202.
        ["vsphere_distributed_port_group"] = Network(["id"], vlan: "vlan_id"),
        ["vsphere_network"] = Network(["id"]),
        // A host port group is never a NIC's network_id; VMs reach it through a
        // vsphere_network data source, which is folded into it below.
        ["vsphere_host_port_group"] = Network([], vlan: "vlan_id"),
        ["aws_subnet"] = Network(["id"], ["tags.Name", "cidr_block"]),
        ["aws_default_subnet"] = Network(["id"], ["tags.Name", "cidr_block"]),
        ["azurerm_subnet"] = Network(["id"]),
    };

    private static readonly Dictionary<string, TopologyInterfaceType> BuiltInInterfaces = new(StringComparer.Ordinal)
    {
        // Every IP configuration of an Azure NIC is in the same virtual
        // network; the first names the subnet the NIC is drawn on.
        ["azurerm_network_interface"] = new() { Keys = ["id"], Network = "ip_configuration.0.subnet_id" },
    };

    private static readonly (string Alias, string Name)[] ProxmoxAliases =
    [
        ("proxmox_vm", "proxmox_virtual_environment_vm2"),
        ("proxmox_cloned_vm", "proxmox_virtual_environment_cloned_vm"),
        ("proxmox_sdn_vnet", "proxmox_virtual_environment_sdn_vnet"),
        ("proxmox_network_linux_bridge", "proxmox_virtual_environment_network_linux_bridge"),
    ];

    static PlanTopologyParser()
    {
        foreach (var (alias, name) in ProxmoxAliases)
        {
            if (BuiltInMachines.TryGetValue(name, out var machine))
            {
                BuiltInMachines[alias] = machine;
            }
            else
            {
                BuiltInNetworks[alias] = BuiltInNetworks[name];
            }
        }
    }

    /// <summary>The resource types drawn when <see cref="TopologyOptions"/> adds none, for the UI's legend and the docs.</summary>
    public static IReadOnlyCollection<string> Types =>
        [.. BuiltInMachines.Keys, .. BuiltInNetworks.Keys, .. BuiltInInterfaces.Keys];

    private sealed record TypeTable(
        Dictionary<string, TopologyMachineType> Machines,
        Dictionary<string, TopologyNetworkType> Networks,
        Dictionary<string, TopologyInterfaceType> Interfaces);

    /// <summary>The built-in types, with any configured under the same name replacing them.</summary>
    private static TypeTable Resolve(TopologyOptions options)
    {
        var machines = new Dictionary<string, TopologyMachineType>(BuiltInMachines, StringComparer.Ordinal);
        var networks = new Dictionary<string, TopologyNetworkType>(BuiltInNetworks, StringComparer.Ordinal);
        var interfaces = new Dictionary<string, TopologyInterfaceType>(BuiltInInterfaces, StringComparer.Ordinal);

        foreach (var (name, type) in options?.Machines ?? [])
        {
            networks.Remove(name);
            interfaces.Remove(name);
            machines[name] = type;
        }

        foreach (var (name, type) in options?.Networks ?? [])
        {
            machines.Remove(name);
            interfaces.Remove(name);
            networks[name] = type;
        }

        foreach (var (name, type) in options?.Interfaces ?? [])
        {
            machines.Remove(name);
            networks.Remove(name);
            interfaces[name] = type;
        }

        return new TypeTable(machines, networks, interfaces);
    }

    public static Models.Topology Parse(string planJson, TopologyOptions options = null)
    {
        using var document = JsonDocument.Parse(planJson);
        var root = document.RootElement;

        return Build(ReadInstances(root), ReadConfiguration(root), Resolve(options), ReadVariables(root));
    }

    /// <summary>
    /// What a state file (format version 4, as Caster stores it on the
    /// workspace) says exists. Every node's <see cref="TopologyNode.Action"/>
    /// is null: nothing is about to change.
    /// </summary>
    /// <remarks>
    /// A state file has no configuration, so a NIC is matched by value only.
    /// After an apply every value is known, so nothing needs the references.
    /// </remarks>
    public static Models.Topology ParseState(string stateJson, TopologyOptions options = null)
    {
        using var document = JsonDocument.Parse(stateJson);

        return Build(ReadStateFile(document.RootElement), [], Resolve(options), []);
    }

    private static Models.Topology Build(
        Dictionary<string, Instance> instances,
        Dictionary<string, JsonElement> configuration,
        TypeTable types,
        Dictionary<string, string> variables)
    {
        var topology = new Models.Topology();
        var networkByKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var networks = new List<Instance>();

        foreach (var instance in instances.Values.Where(x => types.Networks.ContainsKey(x.Type)))
        {
            var type = types.Networks[instance.Type];

            networks.Add(instance);
            var network = new TopologyNode
            {
                Id = instance.Address,
                Kind = TopologyNodeKind.Network,
                Type = instance.Type,
                Label = FirstString(instance.Values, type.Label ?? ["name"]) ?? instance.Name,
                Module = instance.Module,
                Action = instance.Action,
            };

            if (Vlan(instance.Values, type.Vlan) is { } tag)
            {
                network.Properties["vlan"] = tag;
            }

            topology.Nodes.Add(network);

            foreach (var key in type.Keys ?? [])
            {
                var value = GetScalar(instance.Values, key);

                if (!string.IsNullOrEmpty(value))
                {
                    networkByKey.TryAdd(value, instance.Address);
                }
            }
        }

        var aliases = FoldNetworkDataSources(networks, configuration, topology);

        var interfaces = instances.Values.Where(x => types.Interfaces.ContainsKey(x.Type)).ToList();
        var interfaceByKey = new Dictionary<string, Instance>(StringComparer.Ordinal);

        foreach (var instance in interfaces)
        {
            foreach (var key in types.Interfaces[instance.Type].Keys ?? [])
            {
                var value = GetScalar(instance.Values, key);

                if (!string.IsNullOrEmpty(value))
                {
                    interfaceByKey.TryAdd(value, instance);
                }
            }
        }

        var machines = new List<(Instance Instance, TopologyNode Node)>();

        // A machine data source is a template lookup, not a machine in the
        // topology. Networks are different: vsphere_network only exists as one.
        foreach (var instance in instances.Values.Where(x => x.Mode == "managed" && types.Machines.ContainsKey(x.Type)))
        {
            var type = types.Machines[instance.Type];
            var node = new TopologyNode
            {
                Id = instance.Address,
                Kind = TopologyNodeKind.Machine,
                Type = instance.Type,
                Label = FirstString(instance.Values, type.Label ?? ["name"]) ?? instance.Name,
                Module = instance.Module,
                Action = instance.Action,
            };

            foreach (var (name, path) in type.Properties ?? [])
            {
                var value = GetScalar(instance.Values, path);

                if (value != null)
                {
                    node.Properties[name] = value;
                }
            }

            topology.Nodes.Add(node);
            machines.Add((instance, node));

            var nics = Nics(instance.Values, type);
            JsonElement? expressions = configuration.TryGetValue(ConfigAddress(instance.Address), out var found) ? found : null;

            for (var i = 0; i < nics.Count; i++)
            {
                var value = GetScalar(nics[i], type.Network);
                var vlan = Vlan(nics[i], type.Vlan);
                string target = null;

                if (!string.IsNullOrEmpty(value) && vlan != null)
                {
                    target = AddExternal(topology, $"{value}.{vlan}", vlan);
                }
                else if (!string.IsNullOrEmpty(value))
                {
                    if (!networkByKey.TryGetValue(value, out target))
                    {
                        target = AddExternal(topology, value);
                    }
                }
                else
                {
                    target = ResolveReference(instance, expressions, type, i, networks);
                }

                if (target == null)
                {
                    continue;
                }

                topology.Edges.Add(new TopologyEdge
                {
                    From = instance.Address,
                    To = aliases.GetValueOrDefault(target, target),
                    Nic = i,
                });
            }

            var attached = Interfaces(instance, expressions, type, interfaces, interfaceByKey);

            for (var i = 0; i < attached.Count; i++)
            {
                var target = InterfaceNetwork(attached[i], types.Interfaces[attached[i].Type], configuration, networks, networkByKey, topology);

                if (target == null)
                {
                    continue;
                }

                topology.Edges.Add(new TopologyEdge
                {
                    From = instance.Address,
                    To = aliases.GetValueOrDefault(target, target),
                    Nic = nics.Count + i,
                });
            }
        }

        ReadConsoles(instances, configuration, machines, variables);

        topology.Nodes = [.. topology.Nodes.OrderBy(x => x.Kind).ThenBy(x => x.Id, StringComparer.Ordinal)];

        return topology;
    }

    private const string PlayerVm = "crucible_player_virtual_machine";
    private const string PlayerTeam = "crucible_player_team";
    private const string PlayerView = "crucible_player_view";

    private static readonly string[] GuestInfoTeamIds = ["guestinfo.teamId", "guestinfo.team_id"];

    /// <summary>
    /// Which Player teams can open each machine's console: the teams of the
    /// <c>crucible_player_virtual_machine</c> records that point at it, or a
    /// vSphere machine's own <c>guestinfo.teamId</c>, which Caster's Player
    /// sync registers. A record is matched by VM id, else by a reference to
    /// the machine, else by name alone. Nothing is said about any machine when
    /// the workspace uses neither.
    /// </summary>
    private static void ReadConsoles(
        Dictionary<string, Instance> instances,
        Dictionary<string, JsonElement> configuration,
        List<(Instance Instance, TopologyNode Node)> machines,
        Dictionary<string, string> variables)
    {
        var records = instances.Values.Where(x => x.Mode == "managed" && x.Type == PlayerVm).ToList();
        var consoles = new Dictionary<TopologyNode, TopologyConsole>();

        foreach (var (instance, node) in machines)
        {
            if (GuestInfoTeams(instance.Values) is { } teams)
            {
                // Caster's Player sync registers the machine under its own id.
                consoles[node] = new TopologyConsole
                {
                    TeamIds = teams,
                    Match = TopologyConsoleMatch.Self,
                    VmId = GetScalar(instance.Values, "id"),
                };
            }
        }

        if (records.Count == 0 && consoles.Count == 0)
        {
            return;
        }

        var byId = new Dictionary<string, TopologyNode>(StringComparer.Ordinal);

        foreach (var (instance, node) in machines)
        {
            if (PlayerId(instance.Values) is { } id)
            {
                byId.TryAdd(id, node);
            }
        }

        foreach (var record in records)
        {
            TopologyNode node = null;
            var match = TopologyConsoleMatch.Id;

            if (RecordId(record.Values) is { } id)
            {
                node = byId.GetValueOrDefault(id);
            }

            if (node == null && configuration.TryGetValue(ConfigAddress(record.Address), out var expressions))
            {
                match = TopologyConsoleMatch.Reference;
                node = ReferencesAt(expressions, "proxmox_vm_info.0.id")
                    .Concat(References(expressions, "virtual_machine_id"))
                    .Select(reference => FindInModule(machines.Select(x => x.Instance), record.Module, reference))
                    .Where(x => x != null)
                    .Select(x => machines.First(m => m.Instance == x).Node)
                    .FirstOrDefault();
            }

            if (node == null && GetScalar(record.Values, "name") is { Length: > 0 } name)
            {
                match = TopologyConsoleMatch.Name;
                var named = machines.Where(x => x.Node.Label == name).ToList();
                node = named.Count == 1 ? named[0].Node : null;
            }

            if (node == null)
            {
                continue;
            }

            var console = consoles.TryGetValue(node, out var found) ? found : consoles[node] = new TopologyConsole { Match = match };

            // Two records for one machine: the surer match describes both.
            if (console.Match == null || match < console.Match)
            {
                console.Match = match;
            }

            // A machine with two records keeps the first one's console.
            if (console.VmId == null && GetScalar(record.Values, "id") is { } vmId)
            {
                console.VmId = vmId;
                console.Url = GetScalar(record.Values, "url");
            }

            if (record.Values.ValueKind == JsonValueKind.Object &&
                record.Values.TryGetProperty("team_ids", out var teamIds) &&
                teamIds.ValueKind == JsonValueKind.Array)
            {
                console.TeamIds = [.. console.TeamIds.Union(teamIds.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString()))];
            }
            else
            {
                console.TeamsUnknown = true;
            }
        }

        var teamNames = TeamNames(instances, variables);

        foreach (var (_, node) in machines)
        {
            node.Console = consoles.GetValueOrDefault(node) ?? new TopologyConsole();
            node.Console.Teams = [.. node.Console.TeamIds.Select(id => teamNames.GetValueOrDefault(id) ?? new TopologyTeam { Id = id })];
        }
    }

    /// <summary>
    /// Team names the Terraform knows: teams the workspace creates, by their
    /// own name, then teams passed in as a root variable, by the variable's
    /// name, which is how Alloy passes in its view's teams.
    /// </summary>
    private static Dictionary<string, TopologyTeam> TeamNames(
        Dictionary<string, Instance> instances,
        Dictionary<string, string> variables)
    {
        var teams = new Dictionary<string, TopologyTeam>(StringComparer.OrdinalIgnoreCase);

        void Add(string id, string name, string variable = null)
        {
            if (id != null && name != null)
            {
                teams.TryAdd(id, new TopologyTeam { Id = id, Name = name, Variable = variable });
            }
        }

        foreach (var instance in instances.Values.Where(x => x.Mode == "managed"))
        {
            if (instance.Type == PlayerTeam)
            {
                Add(GetScalar(instance.Values, "id"), GetScalar(instance.Values, "name"));
            }
            else if (instance.Type == PlayerView &&
                instance.Values.ValueKind == JsonValueKind.Object &&
                instance.Values.TryGetProperty("team", out var viewTeams) &&
                viewTeams.ValueKind == JsonValueKind.Array)
            {
                foreach (var team in viewTeams.EnumerateArray())
                {
                    Add(GetScalar(team, "team_id"), GetScalar(team, "name"));
                }
            }
        }

        foreach (var (name, value) in variables.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            Add(value, name, name);
        }

        return teams;
    }

    /// <summary>The plan's root variables that hold a single id.</summary>
    private static Dictionary<string, string> ReadVariables(JsonElement root)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);

        if (root.TryGetProperty("variables", out var all) && all.ValueKind == JsonValueKind.Object)
        {
            foreach (var variable in all.EnumerateObject())
            {
                if (variable.Value.ValueKind == JsonValueKind.Object &&
                    variable.Value.TryGetProperty("value", out var value) &&
                    value.ValueKind == JsonValueKind.String &&
                    Guid.TryParse(value.GetString(), out _))
                {
                    variables[variable.Name] = value.GetString();
                }
            }
        }

        return variables;
    }

    /// <summary>A vSphere machine's teams in <c>extra_config</c>, comma-separated, as Caster's Player sync reads them.</summary>
    private static List<string> GuestInfoTeams(JsonElement values)
    {
        if (values.ValueKind != JsonValueKind.Object ||
            !values.TryGetProperty("extra_config", out var extraConfig) ||
            extraConfig.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var teams = GuestInfoTeamIds
            .Select(x => extraConfig.TryGetProperty(x, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null)
            .Where(x => x != null)
            .SelectMany(x => x.Split(','))
            .Select(x => x.Trim())
            .Where(x => Guid.TryParse(x, out _))
            .Distinct()
            .ToList();

        return teams.Count > 0 ? teams : null;
    }

    /// <summary>
    /// The VM id a Player record would use for a machine: a Proxmox VM's node
    /// and id, or a vSphere VM's uuid. Null until apply when it isn't set.
    /// </summary>
    private static string PlayerId(JsonElement values)
    {
        var vmId = GetScalar(values, "vm_id") ?? GetScalar(values, "vmid");
        var node = GetScalar(values, "node_name") ?? GetScalar(values, "target_node");

        if (vmId != null && node != null)
        {
            return $"proxmox:{node}/{vmId}";
        }

        var uuid = GetScalar(values, "uuid");

        return uuid == null ? null : "vm:" + uuid;
    }

    private static string RecordId(JsonElement values)
    {
        var vmId = GetScalar(values, "proxmox_vm_info.0.id");
        var node = GetScalar(values, "proxmox_vm_info.0.node");

        if (vmId != null && node != null)
        {
            return $"proxmox:{node}/{vmId}";
        }

        var id = GetScalar(values, "virtual_machine_id");

        return id == null ? null : "vm:" + id;
    }

    /// <summary>
    /// A <c>data "vsphere_network"</c> whose name references a port group in the
    /// same module is that port group seen from the VM's side. Drawing both
    /// would show every vSphere segment twice, so the data source is dropped and
    /// its NICs are pointed at the port group.
    /// </summary>
    private static Dictionary<string, string> FoldNetworkDataSources(
        List<Instance> networks,
        Dictionary<string, JsonElement> configuration,
        Models.Topology topology)
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var network in networks.Where(x => x.Mode == "data"))
        {
            if (!configuration.TryGetValue(ConfigAddress(network.Address), out var expressions))
            {
                continue;
            }

            var target = References(expressions, "name")
                .Select(reference => FindInModule(networks.Where(x => x.Mode == "managed"), network.Module, reference))
                .FirstOrDefault(x => x != null);

            if (target != null)
            {
                aliases[network.Address] = target.Address;
                topology.Nodes.RemoveAll(x => x.Id == network.Address);
            }
        }

        return aliases;
    }

    /// <summary>
    /// The NIC resources a machine lists in <see cref="TopologyMachineType.Interfaces"/>,
    /// in order. Their ids are not known until apply when they are created in
    /// the same plan, so then the list's references are used: one per NIC, or
    /// a block indexed by <c>each.key</c>, which is the machine's own key.
    /// </summary>
    private static List<Instance> Interfaces(
        Instance machine,
        JsonElement? expressions,
        TopologyMachineType type,
        List<Instance> interfaces,
        Dictionary<string, Instance> interfaceByKey)
    {
        if (string.IsNullOrEmpty(type.Interfaces))
        {
            return [];
        }

        var ids = GetArray(machine.Values, type.Interfaces)
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString())
            .ToList();

        if (ids.Count > 0)
        {
            return [.. ids.Select(x => interfaceByKey.GetValueOrDefault(x)).Where(x => x != null)];
        }

        if (expressions == null)
        {
            return [];
        }

        var references = References(expressions.Value, type.Interfaces).ToList();
        var found = new List<Instance>();

        if (references.Contains("each.key") && IndexPattern().Matches(machine.Address).LastOrDefault() is { } key &&
            key.Index + key.Length == machine.Address.Length)
        {
            references = [.. references.Select(x => x + key.Value)];
        }

        foreach (var reference in references)
        {
            var nic = FindInModule(interfaces, machine.Module, reference);

            if (nic != null && !found.Contains(nic))
            {
                found.Add(nic);
            }
        }

        return found;
    }

    /// <summary>The network a NIC resource is on, by value or else by its configuration's references.</summary>
    private static string InterfaceNetwork(
        Instance nic,
        TopologyInterfaceType type,
        Dictionary<string, JsonElement> configuration,
        List<Instance> networks,
        Dictionary<string, string> networkByKey,
        Models.Topology topology)
    {
        if (string.IsNullOrEmpty(type.Network))
        {
            return null;
        }

        var value = GetScalar(nic.Values, type.Network);

        if (!string.IsNullOrEmpty(value))
        {
            return networkByKey.TryGetValue(value, out var known) ? known : AddExternal(topology, value);
        }

        return configuration.TryGetValue(ConfigAddress(nic.Address), out var expressions)
            ? ReferencesAt(expressions, type.Network)
                .Select(reference => FindInModule(networks, nic.Module, reference)?.Address)
                .FirstOrDefault(x => x != null)
            : null;
    }

    private static string ResolveReference(
        Instance machine,
        JsonElement? expressions,
        TopologyMachineType type,
        int index,
        List<Instance> networks)
    {
        JsonElement nic;

        if (expressions == null)
        {
            return null;
        }
        else if (string.IsNullOrEmpty(type.Nics))
        {
            nic = expressions.Value;
        }
        else if (expressions.Value.TryGetProperty(type.Nics, out var blocks) &&
            blocks.ValueKind == JsonValueKind.Array &&
            blocks.GetArrayLength() > index)
        {
            nic = blocks[index];
        }
        else
        {
            // A dynamic block, or a map of NICs, has one expression for all of
            // them, so it cannot say which NIC goes where.
            return null;
        }

        return References(nic, type.Network)
            .Select(reference => FindInModule(networks, machine.Module, reference)?.Address)
            .FirstOrDefault(x => x != null);
    }

    /// <summary>
    /// Finds the network a reference such as <c>data.vsphere_network.course</c>
    /// or <c>proxmox_sdn_vnet.lan[0].id</c> points at, among the networks in
    /// the same module instance. A reference to a resource with several
    /// instances and no index is ambiguous and resolves to nothing.
    /// </summary>
    private static Instance FindInModule(IEnumerable<Instance> networks, string module, string reference)
    {
        var prefix = module == null ? string.Empty : module + ".";
        var candidates = networks.Where(x => x.Module == module).ToList();

        var exact = candidates.FirstOrDefault(x => x.Address == prefix + reference);

        if (exact != null)
        {
            return exact;
        }

        var matches = candidates.Where(x => ConfigAddress(x.Address) == ConfigAddress(prefix + reference)).ToList();

        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>
    /// A machine's NICs in slot order. A machine with no <see cref="TopologyMachineType.Nics"/>
    /// is its own one NIC; one with no <see cref="TopologyMachineType.Network"/> has none.
    /// </summary>
    private static List<JsonElement> Nics(JsonElement values, TopologyMachineType type)
    {
        if (string.IsNullOrEmpty(type.Network) || values.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        if (string.IsNullOrEmpty(type.Nics))
        {
            return [values];
        }

        if (!values.TryGetProperty(type.Nics, out var nics))
        {
            return [];
        }

        return nics.ValueKind switch
        {
            JsonValueKind.Array => [.. nics.EnumerateArray()],
            // Keyed by slot: net0, net1, ... net10.
            JsonValueKind.Object => [.. nics.EnumerateObject()
                .OrderBy(x => x.Name.Length)
                .ThenBy(x => x.Name, StringComparer.Ordinal)
                .Select(x => x.Value)],
            _ => [],
        };
    }

    private static string AddExternal(Models.Topology topology, string value, string vlan = null)
    {
        var id = "external:" + value;

        if (!topology.Nodes.Any(x => x.Id == id))
        {
            var node = new TopologyNode
            {
                Id = id,
                Kind = TopologyNodeKind.Network,
                Label = value,
                External = true,
            };

            if (vlan != null)
            {
                node.Properties["vlan"] = vlan;
            }

            topology.Nodes.Add(node);
        }

        return id;
    }

    /// <summary>
    /// A VLAN tag, or null for none. Every provider spells "untagged" as 0 or
    /// leaves it out; the older proxmox_vm_qemu used -1.
    /// </summary>
    private static string Vlan(JsonElement element, string path)
    {
        var value = path == null ? null : GetScalar(element, path);

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tag) && tag > 0
            ? value
            : null;
    }

    #region Plan JSON

    private sealed class Instance
    {
        public string Address { get; init; }
        public string Mode { get; init; }
        public string Type { get; init; }
        public string Name { get; init; }
        public string Module { get; init; }
        public string Action { get; init; }
        public JsonElement Values { get; init; }
    }

    /// <summary>
    /// Every resource instance the plan knows about. resource_changes has the
    /// managed resources, with their planned values; data sources read during
    /// planning only appear in prior_state.
    /// </summary>
    private static Dictionary<string, Instance> ReadInstances(JsonElement root)
    {
        var instances = new Dictionary<string, Instance>(StringComparer.Ordinal);

        if (root.TryGetProperty("resource_changes", out var changes) && changes.ValueKind == JsonValueKind.Array)
        {
            foreach (var change in changes.EnumerateArray())
            {
                // A deposed object is the old half of a create-before-destroy
                // replace; the current object already stands for it.
                if (change.TryGetProperty("deposed", out var deposed) && deposed.ValueKind == JsonValueKind.String)
                {
                    continue;
                }

                var body = change.GetProperty("change");
                var values = body.TryGetProperty("after", out var after) && after.ValueKind == JsonValueKind.Object
                    ? after
                    : body.TryGetProperty("before", out var before) ? before : default;

                var address = GetString(change, "address");

                instances[address] = new Instance
                {
                    Address = address,
                    Mode = GetString(change, "mode"),
                    Type = GetString(change, "type"),
                    Name = GetString(change, "name"),
                    Module = GetString(change, "module_address"),
                    Action = ReadAction(body),
                    Values = values,
                };
            }
        }

        if (root.TryGetProperty("prior_state", out var prior) &&
            prior.TryGetProperty("values", out var priorValues) &&
            priorValues.TryGetProperty("root_module", out var rootModule))
        {
            ReadStateModule(rootModule, instances);
        }

        return instances;
    }

    private static void ReadStateModule(JsonElement module, Dictionary<string, Instance> instances)
    {
        if (module.TryGetProperty("resources", out var resources))
        {
            foreach (var resource in resources.EnumerateArray())
            {
                var address = GetString(resource, "address");

                if (instances.ContainsKey(address))
                {
                    continue;
                }

                var moduleAddress = GetString(module, "address");

                instances[address] = new Instance
                {
                    Address = address,
                    Mode = GetString(resource, "mode"),
                    Type = GetString(resource, "type"),
                    Name = GetString(resource, "name"),
                    Module = moduleAddress,
                    Action = "no-op",
                    Values = resource.TryGetProperty("values", out var values) ? values : default,
                };
            }
        }

        if (module.TryGetProperty("child_modules", out var children))
        {
            foreach (var child in children.EnumerateArray())
            {
                ReadStateModule(child, instances);
            }
        }
    }

    /// <summary>
    /// The instances in a state file. Unlike <c>terraform show -json</c>, it
    /// gives no addresses, so they are built the way Terraform writes them:
    /// <c>module.host["dc"].proxmox_virtual_environment_vm.vm</c>.
    /// </summary>
    private static Dictionary<string, Instance> ReadStateFile(JsonElement root)
    {
        var instances = new Dictionary<string, Instance>(StringComparer.Ordinal);

        if (!root.TryGetProperty("resources", out var resources) || resources.ValueKind != JsonValueKind.Array)
        {
            return instances;
        }

        foreach (var resource in resources.EnumerateArray())
        {
            var module = GetString(resource, "module");
            var mode = GetString(resource, "mode");
            var type = GetString(resource, "type");
            var name = GetString(resource, "name");
            var address = (module == null ? string.Empty : module + ".") +
                (mode == "data" ? "data." : string.Empty) +
                $"{type}.{name}";

            foreach (var instance in GetArray(resource, "instances"))
            {
                var index = instance.TryGetProperty("index_key", out var key)
                    ? key.ValueKind switch
                    {
                        JsonValueKind.String => $"[\"{key.GetString().Replace("\\", "\\\\").Replace("\"", "\\\"")}\"]",
                        JsonValueKind.Number => $"[{key.GetRawText()}]",
                        _ => string.Empty,
                    }
                    : string.Empty;

                instances[address + index] = new Instance
                {
                    Address = address + index,
                    Mode = mode,
                    Type = type,
                    Name = name,
                    Module = module,
                    Values = instance.TryGetProperty("attributes", out var attributes) ? attributes : default,
                };
            }
        }

        return instances;
    }

    private static string ReadAction(JsonElement change)
    {
        if (!change.TryGetProperty("actions", out var actions) || actions.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var list = actions.EnumerateArray().Select(x => x.GetString()).ToList();

        return list.Count == 2 && list.Contains("create") && list.Contains("delete")
            ? "replace"
            : string.Join(",", list);
    }

    /// <summary>
    /// Maps a configuration address (instance address with every index removed,
    /// such as <c>module.host.proxmox_virtual_environment_vm.vm</c>) to that
    /// resource block's expressions.
    /// </summary>
    private static Dictionary<string, JsonElement> ReadConfiguration(JsonElement root)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        if (root.TryGetProperty("configuration", out var configuration) &&
            configuration.TryGetProperty("root_module", out var rootModule))
        {
            ReadConfigurationModule(rootModule, string.Empty, result);
        }

        return result;
    }

    private static void ReadConfigurationModule(JsonElement module, string prefix, Dictionary<string, JsonElement> result)
    {
        if (module.TryGetProperty("resources", out var resources))
        {
            foreach (var resource in resources.EnumerateArray())
            {
                if (resource.TryGetProperty("expressions", out var expressions))
                {
                    result[prefix + GetString(resource, "address")] = expressions;
                }
            }
        }

        if (module.TryGetProperty("module_calls", out var calls))
        {
            foreach (var call in calls.EnumerateObject())
            {
                if (call.Value.TryGetProperty("module", out var child))
                {
                    ReadConfigurationModule(child, $"{prefix}module.{call.Name}.", result);
                }
            }
        }
    }

    [GeneratedRegex(@"\[(?:""(?:[^""\\]|\\.)*""|\d+)\]")]
    private static partial Regex IndexPattern();

    private static string ConfigAddress(string address) => IndexPattern().Replace(address, string.Empty);

    private static IEnumerable<string> References(JsonElement expressions, string attribute)
    {
        if (expressions.ValueKind == JsonValueKind.Object &&
            expressions.TryGetProperty(attribute, out var expression) &&
            expression.ValueKind == JsonValueKind.Object &&
            expression.TryGetProperty("references", out var references))
        {
            foreach (var reference in references.EnumerateArray())
            {
                // Terraform lists "x.y.id" and then "x.y"; try the bare
                // resource first by also offering the address without the
                // trailing attribute.
                var text = reference.GetString();
                yield return text;

                var lastDot = text.LastIndexOf('.');
                if (lastDot > 0)
                {
                    yield return text[..lastDot];
                }
            }
        }
    }

    /// <summary>
    /// <see cref="References"/> for an attribute inside blocks, such as
    /// <c>ip_configuration.0.subnet_id</c>: a block's expressions are a list
    /// of objects, one per block.
    /// </summary>
    private static IEnumerable<string> ReferencesAt(JsonElement expressions, string path)
    {
        var parts = path.Split('.');
        var current = expressions;

        foreach (var part in parts[..^1])
        {
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(part, out var child))
            {
                current = child;
            }
            else if (current.ValueKind == JsonValueKind.Array &&
                int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var i) &&
                i < current.GetArrayLength())
            {
                current = current[i];
            }
            else
            {
                return [];
            }
        }

        return References(current, parts[^1]);
    }

    #endregion

    #region Value access

    private static List<JsonElement> GetArray(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Array
            ? [.. value.EnumerateArray()]
            : [];

    private static string GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string FirstString(JsonElement element, string[] paths) =>
        paths.Select(x => GetScalar(element, x)).FirstOrDefault(x => !string.IsNullOrEmpty(x));

    /// <summary>Follows a dotted path such as <c>cpu.0.cores</c> and returns a string or number as text.</summary>
    private static string GetScalar(JsonElement element, string path)
    {
        var current = element;

        foreach (var part in path.Split('.'))
        {
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(part, out var child))
            {
                current = child;
            }
            else if (current.ValueKind == JsonValueKind.Array &&
                int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var i) &&
                i < current.GetArrayLength())
            {
                current = current[i];
            }
            else
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetRawText(),
            _ => null,
        };
    }

    #endregion
}
