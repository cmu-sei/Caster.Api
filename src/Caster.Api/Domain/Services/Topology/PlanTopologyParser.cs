// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Caster.Api.Domain.Models;

namespace Caster.Api.Domain.Services.Topology;

/// <summary>
/// Reads the machines, networks and NIC attachments out of
/// <c>terraform show -json &lt;plan&gt;</c>.
/// </summary>
/// <remarks>
/// A NIC is matched to a network by value first: the planned
/// <c>bridge</c>/<c>network_id</c> against each network's id. That works
/// through any number of module boundaries and for_each maps, which the
/// configuration's <c>references</c> cannot see past. When the value is not
/// known until apply (a vSphere port group created in the same plan), the
/// NIC's own <c>references</c> are tried instead, within the same module.
/// Only resource types listed in <see cref="Types"/> are drawn; everything
/// else in the plan is ignored.
/// </remarks>
public static partial class PlanTopologyParser
{
    private sealed record MachineType(string Block, string Attribute, string[] Label, (string Name, string Path)[] Properties);

    private sealed record NetworkType(string[] Keys, string[] Label);

    private static readonly Dictionary<string, MachineType> Machines = new(StringComparer.Ordinal)
    {
        ["proxmox_virtual_environment_vm"] = new(
            "network_device", "bridge", ["name"],
            [("cpus", "cpu.0.cores"), ("memory_mb", "memory.0.dedicated"), ("node", "node_name")]),
        ["proxmox_vm_qemu"] = new(
            "network", "bridge", ["name"],
            [("cpus", "cores"), ("memory_mb", "memory"), ("node", "target_node")]),
        ["vsphere_virtual_machine"] = new(
            "network_interface", "network_id", ["name"],
            [("cpus", "num_cpus"), ("memory_mb", "memory")]),
    };

    private static readonly Dictionary<string, NetworkType> Networks = new(StringComparer.Ordinal)
    {
        // A NIC's bridge is the vnet id; the alias is the readable name.
        ["proxmox_sdn_vnet"] = new(["id"], ["alias", "id"]),
        ["proxmox_virtual_environment_network_linux_bridge"] = new(["name"], ["name"]),
        // network_id is the managed object id: dvportgroup-123, network-202.
        ["vsphere_distributed_port_group"] = new(["id"], ["name"]),
        ["vsphere_network"] = new(["id"], ["name"]),
        // A host port group is never a NIC's network_id; VMs reach it through a
        // vsphere_network data source, which is folded into it below.
        ["vsphere_host_port_group"] = new([], ["name"]),
    };

    /// <summary>The resource types the parser draws, for the UI's legend and the docs.</summary>
    public static IReadOnlyCollection<string> Types => [.. Machines.Keys, .. Networks.Keys];

    public static Models.Topology Parse(string planJson)
    {
        using var document = JsonDocument.Parse(planJson);
        var root = document.RootElement;

        var instances = ReadInstances(root);
        var configuration = ReadConfiguration(root);

        var topology = new Models.Topology();
        var networkByKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var networks = new List<Instance>();

        foreach (var instance in instances.Values.Where(x => Networks.ContainsKey(x.Type)))
        {
            var type = Networks[instance.Type];

            networks.Add(instance);
            topology.Nodes.Add(new TopologyNode
            {
                Id = instance.Address,
                Kind = TopologyNodeKind.Network,
                Type = instance.Type,
                Label = FirstString(instance.Values, type.Label) ?? instance.Name,
                Module = instance.Module,
                Action = instance.Action,
            });

            foreach (var key in type.Keys)
            {
                var value = GetString(instance.Values, key);

                if (!string.IsNullOrEmpty(value))
                {
                    networkByKey.TryAdd(value, instance.Address);
                }
            }
        }

        var aliases = FoldNetworkDataSources(networks, configuration, topology);

        // A machine data source is a template lookup, not a machine in the
        // topology. Networks are different: vsphere_network only exists as one.
        foreach (var instance in instances.Values.Where(x => x.Mode == "managed" && Machines.ContainsKey(x.Type)))
        {
            var type = Machines[instance.Type];
            var node = new TopologyNode
            {
                Id = instance.Address,
                Kind = TopologyNodeKind.Machine,
                Type = instance.Type,
                Label = FirstString(instance.Values, type.Label) ?? instance.Name,
                Module = instance.Module,
                Action = instance.Action,
            };

            foreach (var (name, path) in type.Properties)
            {
                var value = GetScalar(instance.Values, path);

                if (value != null)
                {
                    node.Properties[name] = value;
                }
            }

            topology.Nodes.Add(node);

            var nics = GetArray(instance.Values, type.Block);
            JsonElement? expressions = configuration.TryGetValue(ConfigAddress(instance.Address), out var found) ? found : null;

            for (var i = 0; i < nics.Count; i++)
            {
                var value = GetString(nics[i], type.Attribute);
                string target = null;

                if (!string.IsNullOrEmpty(value))
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
        }

        topology.Nodes = [.. topology.Nodes.OrderBy(x => x.Kind).ThenBy(x => x.Id, StringComparer.Ordinal)];

        return topology;
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

    private static string ResolveReference(
        Instance machine,
        JsonElement? expressions,
        MachineType type,
        int index,
        List<Instance> networks)
    {
        if (expressions == null ||
            !expressions.Value.TryGetProperty(type.Block, out var blocks) ||
            blocks.ValueKind != JsonValueKind.Array ||
            blocks.GetArrayLength() <= index)
        {
            // A dynamic block has one expression for all of its NICs, so it
            // cannot say which NIC goes where.
            return null;
        }

        return References(blocks[index], type.Attribute)
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

    private static string AddExternal(Models.Topology topology, string value)
    {
        var id = "external:" + value;

        if (!topology.Nodes.Any(x => x.Id == id))
        {
            topology.Nodes.Add(new TopologyNode
            {
                Id = id,
                Kind = TopologyNodeKind.Network,
                Label = value,
                External = true,
            });
        }

        return id;
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

    private static string FirstString(JsonElement element, string[] names) =>
        names.Select(x => GetString(element, x)).FirstOrDefault(x => !string.IsNullOrEmpty(x));

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
