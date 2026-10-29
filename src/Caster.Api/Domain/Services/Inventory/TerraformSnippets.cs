// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    /// A Proxmox template. bpg clones by VMID and keeps whatever hardware the
    /// resource does not declare. Telmate clones by name and then sets the VM to
    /// exactly what the resource declares, so its snippet has to repeat the
    /// template's hardware, read from <paramref name="config"/> (GET
    /// nodes/{node}/qemu/{vmid}/config). Without that config, and for containers,
    /// whose Telmate behavior has not been tested, only bpg is given.
    /// </summary>
    public static Dictionary<string, string> ProxmoxTemplate(
        string name, long? vmId, string node, string type, IReadOnlyDictionary<string, string> config = null)
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

        if (!container && config is { Count: > 0 })
            snippets[Telmate] = TelmateVm(name, node, config);

        return snippets;
    }

    [GeneratedRegex(@"^(ide|sata|scsi|virtio)(\d+)$")]
    private static partial Regex DiskKey();

    [GeneratedRegex(@"^net(\d+)$")]
    private static partial Regex NetKey();

    [GeneratedRegex(@"^serial(\d+)$")]
    private static partial Regex SerialKey();

    [GeneratedRegex(@"^ipconfig\d+$")]
    private static partial Regex IpConfigKey();

    /// <summary>
    /// Config keys that are not hardware, are the template's own, or match what
    /// Telmate sets anyway.
    /// </summary>
    private static readonly HashSet<string> NotCarried =
    [
        "digest", "meta", "smbios1", "vmgenid", "name", "template", "description", "lock",
        "hotplug", "kvm", "tablet", "onboot", "protection", "sshkeys", "cipassword",
        "cores", "sockets", "cpu", "numa", "balloon", "agent", "memory", "efidisk0", "vga"
    ];

    /// <summary>Config keys copied to a same-named, or renamed, Telmate argument as a string.</summary>
    private static readonly Dictionary<string, string> StringArguments = new()
    {
        ["bios"] = "bios",
        ["boot"] = "boot",
        ["bootdisk"] = "bootdisk",
        ["scsihw"] = "scsihw",
        ["machine"] = "machine",
        ["ostype"] = "qemu_os",
        ["cicustom"] = "cicustom",
        ["ciuser"] = "ciuser",
        ["nameserver"] = "nameserver",
        ["searchdomain"] = "searchdomain"
    };

    private static string TelmateVm(string name, string node, IReadOnlyDictionary<string, string> config)
    {
        var arguments = new List<(string, string)> { ("clone", Quote(name)), ("target_node", Quote(node)) };
        var blocks = new List<string>();
        var disks = new SortedDictionary<string, SortedDictionary<int, string>>(StringComparer.Ordinal);
        var leftOut = new List<string>();

        if (config.TryGetValue("memory", out var memory))
        {
            arguments.Add(("memory", memory));
            // Unset, Proxmox balloons down to the full memory; Telmate's default of 0 turns it off.
            arguments.Add(("balloon", config.GetValueOrDefault("balloon", memory)));
        }

        if (config.TryGetValue("agent", out var agent))
            arguments.Add(("agent", Options(agent, "enabled").GetValueOrDefault("enabled") is "1" or "true" ? "1" : "0"));

        foreach (var (key, value) in config.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            Match match;

            if (StringArguments.TryGetValue(key, out var argument) || IpConfigKey().IsMatch(argument = key))
            {
                arguments.Add((argument, Quote(value)));
            }
            else if ((match = DiskKey().Match(key)).Success)
            {
                var bus = match.Groups[1].Value;

                if (!disks.TryGetValue(bus, out var slots))
                    disks[bus] = slots = [];

                slots[int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)] = Disk(bus, value);
            }
            else if ((match = NetKey().Match(key)).Success)
            {
                blocks.Add(Network(match.Groups[1].Value, value));
            }
            else if ((match = SerialKey().Match(key)).Success)
            {
                blocks.Add(Block("serial", [("id", match.Groups[1].Value), ("type", Quote(value))]));
            }
            else if (!NotCarried.Contains(key))
            {
                leftOut.Add(key);
            }
        }

        // Unset, Proxmox uses kvm64; Telmate's default would be host.
        var cpu = new List<(string, string)>
        {
            ("cores", config.GetValueOrDefault("cores", "1")),
            ("type", Quote(Options(config.GetValueOrDefault("cpu", "kvm64"), "cputype").GetValueOrDefault("cputype", "kvm64")))
        };

        if (config.TryGetValue("sockets", out var sockets))
            cpu.Add(("sockets", sockets));
        if (config.GetValueOrDefault("numa") == "1")
            cpu.Add(("numa", "true"));

        var builder = new StringBuilder("""
            # In a proxmox_vm_qemu resource (Telmate/proxmox 3.x). Telmate sets the clone
            # to exactly what the resource declares, so the template's hardware is repeated.

            """);
        builder.Append(Arguments(arguments));
        builder.Append('\n').Append(Block("cpu", cpu));

        if (disks.Count > 0)
        {
            var buses = disks.Select(bus => $"{bus.Key} {{\n{Indent(string.Concat(bus.Value.Select(slot => $"{bus.Key}{slot.Key} {{\n{Indent(slot.Value)}}}\n")))}}}\n");
            builder.Append($"\ndisks {{\n{Indent(string.Concat(buses))}}}\n");
        }

        if (config.TryGetValue("efidisk0", out var efidisk))
        {
            var efi = Options(efidisk);
            builder.Append('\n').Append(Block("efidisk",
            [
                ("efitype", Quote(efi.GetValueOrDefault("efitype", "4m"))),
                ("storage", Quote(Storage(efidisk))),
                ("pre_enrolled_keys", efi.GetValueOrDefault("pre-enrolled-keys") == "1" ? "true" : "false")
            ]));
        }

        foreach (var block in blocks)
            builder.Append('\n').Append(block);

        if (config.TryGetValue("vga", out var vgaValue))
        {
            var vga = Options(vgaValue, "type");
            var lines = new List<(string, string)> { ("type", Quote(vga.GetValueOrDefault("type", "std"))) };

            if (vga.TryGetValue("memory", out var vgaMemory))
                lines.Add(("memory", vgaMemory));

            builder.Append('\n').Append(Block("vga", lines));
        }

        if (config.ContainsKey("cipassword"))
            builder.Append("\n# The template's cloud-init password cannot be read back, so set cipassword yourself.\n");

        if (leftOut.Count > 0)
            builder.Append($"\n# Not repeated here, so the clone will not have them: {string.Join(", ", leftOut)}\n");

        return builder.ToString();
    }

    /// <summary>
    /// A disk, CD-ROM or cloud-init drive, as the body of its slot block. Telmate only
    /// takes emulatessd on ide, sata and scsi, and iothread on scsi and virtio.
    /// </summary>
    private static string Disk(string bus, string value)
    {
        var options = Options(value);
        var volume = value.Split(',')[0];

        if (volume.Contains("cloudinit", StringComparison.Ordinal))
            return Block("cloudinit", [("storage", Quote(Storage(value)))]);

        if (options.GetValueOrDefault("media") == "cdrom")
        {
            return volume is "none" or "cdrom"
                ? Block("cdrom", [("passthrough", "false")])
                : Block("cdrom", [("iso", Quote(volume))]);
        }

        var lines = new List<(string, string)>
        {
            ("storage", Quote(Storage(value))),
            ("size", Quote(options.GetValueOrDefault("size", "0")))
        };

        if (options.GetValueOrDefault("discard") == "on")
            lines.Add(("discard", "true"));
        if (options.GetValueOrDefault("ssd") == "1" && bus != "virtio")
            lines.Add(("emulatessd", "true"));
        if (options.GetValueOrDefault("iothread") == "1" && bus is "scsi" or "virtio")
            lines.Add(("iothread", "true"));
        if (options.TryGetValue("cache", out var cache))
            lines.Add(("cache", Quote(cache)));
        if (options.GetValueOrDefault("backup") == "0")
            lines.Add(("backup", "false"));

        return Block("disk", lines);
    }

    /// <summary>"virtio=BC:24:11:44:67:F8,bridge=vmbr0,tag=10". The MAC is left out, so the clone gets its own.</summary>
    private static string Network(string id, string value)
    {
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var model = parts.Length > 0 ? parts[0].Split('=')[0] : "virtio";
        var options = Options(value);
        var lines = new List<(string, string)>
        {
            ("id", id),
            ("model", Quote(model)),
            ("bridge", Quote(options.GetValueOrDefault("bridge", "vmbr0")))
        };

        if (options.TryGetValue("tag", out var tag))
            lines.Add(("tag", tag));
        if (options.GetValueOrDefault("firewall") == "1")
            lines.Add(("firewall", "true"));

        return Block("network", lines);
    }

    /// <summary>"local-lvm:vm-105-disk-0,size=4M" is on local-lvm.</summary>
    private static string Storage(string value) => value.Split(',')[0].Split(':')[0];

    /// <summary>
    /// Proxmox's "a=1,b=2" property strings. A bare value, e.g. "1" in "1,fstrim=1"
    /// or "std" in "std,memory=32", is kept under <paramref name="bareKey"/>.
    /// </summary>
    private static Dictionary<string, string> Options(string value, string bareKey = null)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var part in (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = part.IndexOf('=');

            if (equals >= 0)
                options.TryAdd(part[..equals], part[(equals + 1)..]);
            else if (bareKey != null)
                options.TryAdd(bareKey, part);
        }

        return options;
    }

    /// <summary>Arguments one per line, with the = signs lined up as terraform fmt does.</summary>
    private static string Arguments(IReadOnlyList<(string Key, string Value)> arguments)
    {
        var width = arguments.Max(x => x.Key.Length);
        return string.Concat(arguments.Select(x => $"{x.Key.PadRight(width)} = {x.Value}\n"));
    }

    private static string Block(string type, IReadOnlyList<(string Key, string Value)> arguments) =>
        $"{type} {{\n{Indent(Arguments(arguments))}}}\n";

    private static string Indent(string text) =>
        string.Concat(text.Split('\n').SkipLast(1).Select(x => x.Length == 0 ? "\n" : $"  {x}\n"));

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
