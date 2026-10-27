// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Caster.Api.Domain.Services.Inventory;

/// <summary>
/// Terraform that references a discovered VM template, for <see cref="InventoryEntry.Terraform"/>.
/// Each is written to paste as-is: data sources stand alone, and an argument that only
/// makes sense inside a resource says which resource. Labels come from the template's
/// name, so pasting two templates does not declare the same block twice.
/// </summary>
public static partial class TerraformSnippets
{
    public const string Vsphere = "hashicorp/vsphere";
    public const string Bpg = "bpg/proxmox";
    public const string Telmate = "Telmate/proxmox";

    /// <summary>
    /// An inventory template, found by its folder path below the datacenter's VM
    /// folder. Without a datacenter the provider searches the default one, which
    /// only works on a vCenter that has a single datacenter.
    /// </summary>
    public static Dictionary<string, string> VsphereTemplate(string name, string path, string datacenter)
    {
        var label = Label(name);
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(datacenter))
        {
            builder.Append($$"""
                data "vsphere_datacenter" "{{label}}_datacenter" {
                  name = {{Quote(datacenter)}}
                }


                """);
            builder.Append($$"""
                data "vsphere_virtual_machine" "{{label}}" {
                  name          = {{Quote(path ?? name)}}
                  datacenter_id = data.vsphere_datacenter.{{label}}_datacenter.id
                }

                """);
        }
        else
        {
            builder.Append($$"""
                data "vsphere_virtual_machine" "{{label}}" {
                  name = {{Quote(path ?? name)}}
                }

                """);
        }

        builder.Append($$"""

            # In a vsphere_virtual_machine resource:
            #   clone {
            #     template_uuid = data.vsphere_virtual_machine.{{label}}.id
            #   }

            """);

        return new() { [Vsphere] = builder.ToString() };
    }

    /// <summary>
    /// A content library item, found by its name and type in its library.
    /// </summary>
    public static Dictionary<string, string> VsphereLibraryItem(string name, string type, string library)
    {
        var label = Label(name);

        return new()
        {
            [Vsphere] = $$"""
                data "vsphere_content_library" "{{label}}_library" {
                  name = {{Quote(library)}}
                }

                data "vsphere_content_library_item" "{{label}}" {
                  name       = {{Quote(name)}}
                  type       = {{Quote(type)}}
                  library_id = data.vsphere_content_library.{{label}}_library.id
                }

                # In a vsphere_virtual_machine resource:
                #   clone {
                #     template_uuid = data.vsphere_content_library_item.{{label}}.id
                #   }

                """
        };
    }

    /// <summary>
    /// A Proxmox template. bpg clones by VMID and Telmate by name for a VM, so each
    /// provider gets its own. Containers clone by VMID in both.
    /// </summary>
    public static Dictionary<string, string> ProxmoxTemplate(string name, long? vmId, string node, string type)
    {
        var snippets = new Dictionary<string, string>();

        if (vmId is null || string.IsNullOrWhiteSpace(node))
            return snippets;

        var container = type == "lxc";

        snippets[Bpg] = $$"""
            # In a {{(container ? "proxmox_virtual_environment_container" : "proxmox_virtual_environment_vm")}} resource:
            clone {
              vm_id     = {{vmId}}
              node_name = {{Quote(node)}}
            }

            """;

        snippets[Telmate] = container
            ? $$"""
                # In a proxmox_lxc resource:
                clone       = {{Quote(vmId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))}}
                target_node = {{Quote(node)}}

                """
            : $$"""
                # In a proxmox_vm_qemu resource:
                clone       = {{Quote(name)}}
                target_node = {{Quote(node)}}

                """;

        return snippets;
    }

    /// <summary>
    /// A block label from a name: letters, digits and underscores, starting with a letter.
    /// </summary>
    public static string Label(string name)
    {
        var label = NotLabel().Replace((name ?? string.Empty).ToLowerInvariant(), "_").Trim('_');

        if (label.Length == 0)
            return "template";

        return char.IsLetter(label[0]) ? label : $"template_{label}";
    }

    /// <summary>
    /// A quoted HCL string. Template sequences are escaped too, so a name that
    /// happens to contain "${" stays literal.
    /// </summary>
    public static string Quote(string value) =>
        "\"" + (value ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("${", "$${")
            .Replace("%{", "%%{") + "\"";

    [GeneratedRegex("[^a-z0-9_]+")]
    private static partial Regex NotLabel();
}
