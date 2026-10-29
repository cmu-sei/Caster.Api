// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Designs;

namespace Caster.Api.Features.Files;

/// <summary>
/// Reads what Terraform says about the range from HCL: which hypervisors it builds on, the template,
/// network and datastore names it sets as literals, and the networks it creates itself. Regular
/// expressions over the text, not a parser: enough to check a draft, not to plan it.
/// </summary>
public static class AiTerraformReferences
{
    // a top-level block: resource "type" "name" {, data "type" "name" {, variable "name" {
    private static readonly Regex BlockStart = new(
        @"^[ \t]*(resource|data|variable)[ \t]+""([^""]+)""(?:[ \t]+""([^""]+)"")?[ \t]*\{",
        RegexOptions.Multiline);

    private static readonly Regex NameArgument = new(@"^\s*name\s*=\s*""([^""$]+)""", RegexOptions.Multiline);
    private static readonly Regex IdArgument = new(@"^\s*(?:id|vnet)\s*=\s*""([^""$]+)""", RegexOptions.Multiline);
    private static readonly Regex Default = new(@"^\s*default\s*=\s*""([^""$]+)""", RegexOptions.Multiline);
    private static readonly Regex Bridge = new(@"\bbridge\s*=\s*""([^""$]+)""");
    private static readonly Regex CloneByName = new(@"\bclone\s*=\s*""([^""$]+)""");
    private static readonly Regex CloneByVmId = new(@"\bclone\s*\{[^{}]*?\bvm_id\s*=\s*(\d+)", RegexOptions.Singleline);
    private static readonly Regex ProxmoxDatastore = new(@"\b(?:datastore_id|storage)\s*=\s*""([^""$]+)""");
    private static readonly Regex TfvarsAssignment = new(@"^\s*([A-Za-z_][A-Za-z0-9_-]*)\s*=\s*""([^""$]+)""", RegexOptions.Multiline);
    private static readonly Regex ProviderSource = new(@"source\s*=\s*""(?:[^""/]+/)?(?:bpg|telmate|hashicorp|vmware)/(proxmox|vsphere)""", RegexOptions.IgnoreCase);
    private static readonly Regex ResourceType = new(@"^\s*(?:(?:resource|data)\s+""|provider\s+"")(proxmox|vsphere)[_""]", RegexOptions.Multiline | RegexOptions.IgnoreCase);

    // resources that create a network a machine can then attach to by name
    private static readonly Dictionary<string, (string Hypervisor, Regex Name)> NetworkResources = new()
    {
        ["proxmox_virtual_environment_network_linux_bridge"] = (Hypervisors.Proxmox, NameArgument),
        ["proxmox_virtual_environment_network_linux_vlan"] = (Hypervisors.Proxmox, NameArgument),
        ["proxmox_virtual_environment_sdn_vnet"] = (Hypervisors.Proxmox, IdArgument),
        ["proxmox_sdn_vnet"] = (Hypervisors.Proxmox, IdArgument),
        ["vsphere_distributed_port_group"] = (Hypervisors.VSphere, NameArgument),
        ["vsphere_host_port_group"] = (Hypervisors.VSphere, NameArgument),
    };

    /// <summary>
    /// The hypervisors the files build on, from their provider blocks and sources and resource types, in
    /// <see cref="Hypervisors.All"/> order. Empty when they name neither.
    /// </summary>
    public static string[] DetectHypervisors(IEnumerable<string> contents)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var content in contents.Select(Strip))
        {
            foreach (Match m in ProviderSource.Matches(content))
                found.Add(m.Groups[1].Value);
            foreach (Match m in ResourceType.Matches(content))
                found.Add(m.Groups[1].Value);
        }
        return Hypervisors.All.Where(h => found.Contains(h)).ToArray();
    }

    /// <summary>
    /// The template, network and datastore names the file sets as literals.
    /// </summary>
    public static IEnumerable<AiNameUse> NameUses(string fileName, string content)
    {
        content = Strip(content);
        if (fileName.EndsWith(".tfvars", StringComparison.OrdinalIgnoreCase))
        {
            foreach (Match m in TfvarsAssignment.Matches(content))
                if (KindOfVariable(m.Groups[1].Value) is { } kind)
                    yield return new AiNameUse(kind, m.Groups[2].Value, $"{fileName} {m.Groups[1].Value}", ByVmId: IsVmId(kind, m.Groups[2].Value));
            yield break;
        }

        foreach (var (keyword, type, name, body) in Blocks(content))
        {
            var where = keyword == "variable" ? $"{fileName} variable {type}" : $"{fileName} {type}.{name}";
            if (keyword == "variable")
            {
                if (KindOfVariable(type) is { } kind && Default.Match(body) is { Success: true } d)
                    yield return new AiNameUse(kind, d.Groups[1].Value, where, ByVmId: IsVmId(kind, d.Groups[1].Value));
                continue;
            }

            if (keyword == "data" && type.StartsWith("vsphere_", StringComparison.Ordinal))
            {
                var kind = type switch
                {
                    "vsphere_virtual_machine" => AiInventoryKind.Template,
                    "vsphere_network" => AiInventoryKind.Network,
                    "vsphere_datastore" => AiInventoryKind.Datastore,
                    _ => (AiInventoryKind?)null
                };
                if (kind != null && NameArgument.Match(body) is { Success: true } n)
                    yield return new AiNameUse(kind.Value, n.Groups[1].Value, where, Hypervisors.VSphere);
                continue;
            }

            if (keyword == "resource" && (type == "proxmox_virtual_environment_vm" || type == "proxmox_vm_qemu"))
            {
                foreach (Match m in Bridge.Matches(body))
                    yield return new AiNameUse(AiInventoryKind.Network, m.Groups[1].Value, where, Hypervisors.Proxmox);
                foreach (Match m in CloneByVmId.Matches(body))
                    yield return new AiNameUse(AiInventoryKind.Template, m.Groups[1].Value, where, Hypervisors.Proxmox, ByVmId: true);
                foreach (Match m in CloneByName.Matches(body))
                    yield return new AiNameUse(AiInventoryKind.Template, m.Groups[1].Value, where, Hypervisors.Proxmox);
                foreach (Match m in ProxmoxDatastore.Matches(body))
                    yield return new AiNameUse(AiInventoryKind.Datastore, m.Groups[1].Value, where, Hypervisors.Proxmox);
            }
        }
    }

    /// <summary>
    /// The networks the files create, which a draft may use though the inventory does not have them yet.
    /// </summary>
    public static IEnumerable<string> CreatedNetworks(string content)
    {
        foreach (var (keyword, type, _, body) in Blocks(Strip(content)))
            if (keyword == "resource" && NetworkResources.TryGetValue(type, out var resource) && resource.Name.Match(body) is { Success: true } m)
                yield return m.Groups[1].Value;
    }

    /// <summary>
    /// The kind of name a variable holds, from its name: template, network or datastore. Null for any other.
    /// </summary>
    public static AiInventoryKind? KindOfVariable(string name)
    {
        var n = (name ?? "").ToLowerInvariant();
        if (n.Contains("template"))
            return AiInventoryKind.Template;
        if (n.Contains("datastore") || n.Contains("storage"))
            return AiInventoryKind.Datastore;
        if (n.Contains("network") || n.Contains("bridge") || n.Contains("vnet") || n.Contains("port_group") || n.Contains("portgroup"))
            return n.Contains("cidr") || n.Contains("address") || n.Contains("mask") || n.Contains("gateway") ? null : AiInventoryKind.Network;
        return null;
    }

    private static bool IsVmId(AiInventoryKind kind, string value) =>
        kind == AiInventoryKind.Template && value.Length > 0 && value.All(char.IsDigit);

    private static string Strip(string content) => AiTerraformDraftBuilder.StripComments(content);

    /// <summary>
    /// Each top-level resource, data and variable block with its body, found by matching braces
    /// outside strings.
    /// </summary>
    public static IEnumerable<(string Keyword, string Type, string Name, string Body)> Blocks(string content)
    {
        content ??= "";
        foreach (Match m in BlockStart.Matches(content))
        {
            var start = m.Index + m.Length;
            var depth = 1;
            var inString = false;
            var i = start;
            for (; i < content.Length && depth > 0; i++)
            {
                var c = content[i];
                if (inString)
                {
                    if (c == '\\') i++;
                    else if (c == '"') inString = false;
                }
                else if (c == '"') inString = true;
                else if (c == '{') depth++;
                else if (c == '}') depth--;
            }
            yield return (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Success ? m.Groups[3].Value : null, content[start..Math.Max(start, i - 1)]);
        }
    }
}
