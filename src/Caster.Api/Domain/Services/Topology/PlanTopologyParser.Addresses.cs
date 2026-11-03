// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Options;

namespace Caster.Api.Domain.Services.Topology;

/// <summary>
/// The IP addresses on each machine, as
/// <see cref="TopologyNode.Properties"/>:
/// <list type="bullet">
/// <item><c>ip_addresses</c>: comma-separated, in NIC order, IPv4 before IPv6, without prefix lengths.</item>
/// <item><c>gateway</c>: the machine's first IPv4 gateway.</item>
/// <item><c>ip_addresses_by_network</c>: <c>networkNodeId=address;...</c>, for the addresses whose NIC is drawn on a network.</item>
/// </list>
/// </summary>
/// <remarks>
/// Only the built-in types have them; a configured type, even one replacing a
/// built-in, does not. A value Terraform marks as known only after apply, or
/// as sensitive, is left out, and so is anything that is not an address:
/// <c>dhcp</c>, <c>auto</c>, loopback and link-local addresses.
/// </remarks>
public static partial class PlanTopologyParser
{
    #region Machines

    private sealed class Addresses
    {
        /// <summary>What the configuration gives each NIC, by its position on the machine.</summary>
        public List<(int Nic, string Address)> Planned { get; } = [];

        /// <summary>
        /// What the machine reports it has once it runs (vSphere's guest
        /// addresses): preferred over <see cref="Planned"/> unless the plan is
        /// about to change the machine.
        /// </summary>
        public List<string> Reported { get; } = [];

        /// <summary>Addresses on no drawn network, such as a public IP.</summary>
        public List<string> Other { get; } = [];

        public string Gateway { get; private set; }

        public void Plan(int nic, string value)
        {
            if (Address(value) is { } address)
            {
                Planned.Add((nic, address));
            }
        }

        public void Report(string value) => AddTo(Reported, value);

        public void Add(string value) => AddTo(Other, value);

        public void Gate(string value)
        {
            if (Gateway == null && Address(value) is { } address && !address.Contains(':'))
            {
                Gateway = address;
            }
        }

        private static void AddTo(List<string> list, string value)
        {
            if (Address(value) is { } address)
            {
                list.Add(address);
            }
        }
    }

    private static readonly Dictionary<string, Action<Field, Addresses>> AddressReaders = new(StringComparer.Ordinal)
    {
        ["proxmox_virtual_environment_vm"] = CloudInit,
        ["proxmox_virtual_environment_cloned_vm"] = CloudInit,
        ["proxmox_virtual_environment_vm2"] = CloudInit,
        ["proxmox_virtual_environment_container"] = CloudInit,
        ["proxmox_vm_qemu"] = IpConfigStrings,
        ["proxmox_lxc"] = LxcNetwork,
        ["vsphere_virtual_machine"] = VsphereCustomize,
        ["aws_instance"] = AwsInstance,
    };

    /// <summary>bpg's cloud-init <c>initialization.ip_config</c>: one block per NIC, in NIC order.</summary>
    private static void CloudInit(Field values, Addresses found)
    {
        var i = 0;

        foreach (var config in values["initialization"].One["ip_config"].Items)
        {
            var ipv4 = config["ipv4"].One;

            found.Plan(i, ipv4["address"].Text);
            found.Plan(i, config["ipv6"].One["address"].Text);
            found.Gate(ipv4["gateway"].Text);
            i++;
        }
    }

    /// <summary>The telmate provider's <c>ipconfig0</c> to <c>ipconfig15</c>: <c>ip=10.0.0.5/24,gw=10.0.0.1,ip6=...</c>.</summary>
    private static void IpConfigStrings(Field values, Addresses found)
    {
        for (var i = 0; i < 16; i++)
        {
            foreach (var part in (values["ipconfig" + i.ToString(CultureInfo.InvariantCulture)].Text ?? string.Empty).Split(','))
            {
                var pair = part.Split('=', 2);

                switch (pair.Length == 2 ? pair[0].Trim() : null)
                {
                    case "ip" or "ip6":
                        found.Plan(i, pair[1]);
                        break;
                    case "gw":
                        found.Gate(pair[1]);
                        break;
                }
            }
        }
    }

    /// <summary>The telmate provider's container <c>network</c> blocks, each with its own <c>ip</c>, <c>ip6</c> and <c>gw</c>.</summary>
    private static void LxcNetwork(Field values, Addresses found)
    {
        var i = 0;

        foreach (var nic in values["network"].Items)
        {
            found.Plan(i, nic["ip"].Text);
            found.Plan(i, nic["ip6"].Text);
            found.Gate(nic["gw"].Text);
            i++;
        }
    }

    /// <summary>
    /// <c>clone.customize.network_interface</c>, one per NIC in order, and,
    /// once the machine runs, the guest's own <c>default_ip_address</c> and
    /// <c>guest_ip_addresses</c>, which are its real addresses when it uses DHCP.
    /// </summary>
    private static void VsphereCustomize(Field values, Addresses found)
    {
        var customize = values["clone"].One["customize"].One;
        var i = 0;

        foreach (var nic in customize["network_interface"].Items)
        {
            found.Plan(i, nic["ipv4_address"].Text);
            found.Plan(i, nic["ipv6_address"].Text);
            i++;
        }

        found.Gate(customize["ipv4_gateway"].Text);
        found.Report(values["default_ip_address"].Text);

        foreach (var address in values["guest_ip_addresses"].Items)
        {
            found.Report(address.Text);
        }
    }

    /// <summary>The instance is its own NIC on its subnet; the public IP is on no subnet.</summary>
    private static void AwsInstance(Field values, Addresses found)
    {
        found.Plan(0, values["private_ip"].Text);

        foreach (var address in values["ipv6_addresses"].Items)
        {
            found.Plan(0, address.Text);
        }

        found.Add(values["public_ip"].Text);
    }

    /// <summary>
    /// An Azure NIC's <c>ip_configuration</c> private addresses, on the NIC's
    /// network, and the address of any <c>azurerm_public_ip</c> it names.
    /// </summary>
    private static void AzureInterface(Instance nic, int position, Dictionary<string, Instance> instances, Addresses found)
    {
        foreach (var config in Field.Of(nic)["ip_configuration"].Items)
        {
            found.Plan(position, config["private_ip_address"].Text);

            if (config["public_ip_address_id"].Text is { Length: > 0 } id &&
                instances.Values.FirstOrDefault(x => x.Type == "azurerm_public_ip" && GetScalar(x.Values, "id") == id) is { } ip)
            {
                found.Add(Field.Of(ip)["ip_address"].Text);
            }
        }
    }

    private static void AddAddresses(
        Instance instance,
        TopologyNode node,
        TypeTable types,
        int nicCount,
        List<Instance> attached,
        Dictionary<string, Instance> instances,
        Models.Topology topology)
    {
        if (!ReferenceEquals(types.Machines[instance.Type], BuiltInMachines.GetValueOrDefault(instance.Type)))
        {
            return;
        }

        var found = new Addresses();

        if (AddressReaders.TryGetValue(instance.Type, out var read))
        {
            read(Field.Of(instance), found);
        }

        for (var i = 0; i < attached.Count; i++)
        {
            if (attached[i].Type == "azurerm_network_interface" &&
                ReferenceEquals(types.Interfaces[attached[i].Type], BuiltInInterfaces[attached[i].Type]))
            {
                AzureInterface(attached[i], nicCount + i, instances, found);
            }
        }

        // Read from the state, or a plan that leaves the machine alone, the
        // guest's own addresses are what it has now.
        var planned = found.Planned.Select(x => x.Address).ToList();
        var current = instance.Action is null or "no-op" or "read";
        var chosen = current && found.Reported.Count > 0 ? found.Reported
            : planned.Count > 0 ? planned
            : found.Reported;

        var addresses = chosen
            .Concat(found.Other)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x.Contains(':') ? 1 : 0)
            .ToList();

        if (addresses.Count == 0)
        {
            return;
        }

        node.Properties["ip_addresses"] = string.Join(",", addresses);

        if (found.Gateway != null)
        {
            node.Properties["gateway"] = found.Gateway;
        }

        var shown = addresses.ToHashSet(StringComparer.Ordinal);
        var networks = topology.Edges
            .Where(x => x.From == instance.Address)
            .GroupBy(x => x.Nic)
            .ToDictionary(x => x.Key, x => x.First().To);
        var byNetwork = found.Planned
            .Where(x => shown.Contains(x.Address) && networks.ContainsKey(x.Nic))
            .OrderBy(x => x.Address.Contains(':') ? 1 : 0)
            .Select(x => $"{networks[x.Nic]}={x.Address}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (byNetwork.Count > 0)
        {
            node.Properties["ip_addresses_by_network"] = string.Join(";", byNetwork);
        }
    }

    /// <summary>
    /// The canonical text of an address, without any prefix length, or null
    /// for anything else: <c>dhcp</c>, <c>auto</c>, loopback, link-local.
    /// </summary>
    private static string Address(string value)
    {
        var text = value?.Trim();

        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var slash = text.IndexOf('/');

        if (slash >= 0)
        {
            text = text[..slash];
        }

        if (!IPAddress.TryParse(text, out var address) ||
            IPAddress.IsLoopback(address) ||
            address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any) ||
            address.IsIPv6LinkLocal ||
            (address.AddressFamily == AddressFamily.InterNetwork && address.GetAddressBytes() is [169, 254, ..]))
        {
            return null;
        }

        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4().ToString() : address.ToString();
    }

    #endregion

}
