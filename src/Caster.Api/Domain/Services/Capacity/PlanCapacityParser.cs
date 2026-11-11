// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Caster.Api.Infrastructure.Options;

namespace Caster.Api.Domain.Services.Capacity;

/// <summary>
/// Reads the machines out of <c>terraform show -json &lt;plan&gt;</c>, with the
/// vCPUs, memory and disks each has before and after the plan, and the host
/// and datastores the plan puts it on.
/// </summary>
/// <remarks>
/// Every value comes from the planned <c>before</c> and <c>after</c> objects of
/// <c>resource_changes</c>. Where the plan leaves out an attribute the provider
/// has a default for, the default is used: bpg gives a VM 1 core, 1 socket,
/// 512 MiB and an 8 GiB disk. A value only known after apply is not counted,
/// and the machine is marked <see cref="MachineSize.Incomplete"/>. A machine
/// that is in the prior state and not in <c>resource_changes</c> (a targeted
/// run) is counted as it is, so a workspace's totals still include it.
/// </remarks>
public static partial class PlanCapacityParser
{
    private delegate MachineSize SizeReader(JsonElement values, bool cloned);

    private sealed record MachineType(string Hypervisor, SizeReader Size, Func<JsonElement, string> Host, Func<JsonElement, bool> Cloned);

    private static readonly MachineType BpgVm = new(InfrastructureProviders.Proxmox, BpgVmSize, x => Scalar(x, "node_name"), x => Array(x, "clone").Count > 0);
    private static readonly MachineType BpgCloned = new(InfrastructureProviders.Proxmox, BpgClonedSize, x => Scalar(x, "node_name"), _ => true);
    private static readonly MachineType BpgVm2 = new(InfrastructureProviders.Proxmox, BpgVm2Size, x => Scalar(x, "node_name"), x => Present(x, "clone"));
    private static readonly MachineType BpgContainer = new(InfrastructureProviders.Proxmox, BpgContainerSize, x => Scalar(x, "node_name"), x => Array(x, "clone").Count > 0);
    private static readonly MachineType TelmateQemu = new(InfrastructureProviders.Proxmox, TelmateQemuSize, x => Scalar(x, "target_node"), x => !string.IsNullOrEmpty(Scalar(x, "clone")) || !string.IsNullOrEmpty(Scalar(x, "clone_id")));
    private static readonly MachineType TelmateLxc = new(InfrastructureProviders.Proxmox, TelmateLxcSize, x => Scalar(x, "target_node"), x => !string.IsNullOrEmpty(Scalar(x, "clone")));
    private static readonly MachineType Vsphere = new(InfrastructureProviders.Vsphere, VsphereSize, x => Scalar(x, "host_system_id"), x => Array(x, "clone").Count > 0);
    private static readonly MachineType Cloud = new(null, (_, _) => new MachineSize { Incomplete = true }, _ => null, _ => false);

    /// <summary>The resource types counted as machines. bpg's short aliases are listed under both names.</summary>
    private static readonly Dictionary<string, MachineType> Types = new(StringComparer.Ordinal)
    {
        ["proxmox_virtual_environment_vm"] = BpgVm,
        ["proxmox_virtual_environment_cloned_vm"] = BpgCloned,
        ["proxmox_cloned_vm"] = BpgCloned,
        ["proxmox_virtual_environment_vm2"] = BpgVm2,
        ["proxmox_vm"] = BpgVm2,
        ["proxmox_virtual_environment_container"] = BpgContainer,
        ["proxmox_vm_qemu"] = TelmateQemu,
        ["proxmox_lxc"] = TelmateLxc,
        ["vsphere_virtual_machine"] = Vsphere,
        // Counted against MaxMachines; their size is an instance type, and they take no room on a hypervisor.
        ["aws_instance"] = Cloud,
        ["azurerm_linux_virtual_machine"] = Cloud,
        ["azurerm_windows_virtual_machine"] = Cloud,
        ["azurerm_virtual_machine"] = Cloud,
    };

    /// <summary>The resource types counted as machines, for the docs.</summary>
    public static IReadOnlyCollection<string> MachineTypes => Types.Keys;

    public static DeploymentDemand Parse(string planJson)
    {
        using var document = JsonDocument.Parse(planJson);
        var root = document.RootElement;
        var demand = new DeploymentDemand();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (root.TryGetProperty("resource_changes", out var changes) && changes.ValueKind == JsonValueKind.Array)
        {
            foreach (var change in changes.EnumerateArray())
            {
                var address = String(change, "address");
                var type = String(change, "type");

                if (String(change, "mode") != "managed" || type == null || !Types.TryGetValue(type, out var machineType))
                    continue;

                // The old half of a create-before-destroy replace: the current object stands for it.
                if (change.TryGetProperty("deposed", out var deposed) && deposed.ValueKind == JsonValueKind.String)
                    continue;

                var body = change.GetProperty("change");
                var action = ReadAction(body);

                if (action == "read")
                    continue;

                var before = Object(body, "before");
                var after = action == "delete" ? default : Object(body, "after");

                var machine = new PlannedMachine
                {
                    Address = address,
                    Type = type,
                    Hypervisor = machineType.Hypervisor,
                    Action = action,
                    Host = machineType.Host(after.ValueKind == JsonValueKind.Object ? after : before),
                    Before = before.ValueKind == JsonValueKind.Object ? machineType.Size(before, machineType.Cloned(before)) : null,
                    After = after.ValueKind == JsonValueKind.Object ? machineType.Size(after, machineType.Cloned(after)) : null,
                };

                // A value known only after apply is missing from after, and true in after_unknown.
                if (machine.After != null && HasUnknown(body))
                    machine.After.Incomplete = true;

                seen.Add(address);
                demand.Machines.Add(machine);
            }
        }

        if (root.TryGetProperty("prior_state", out var prior) &&
            prior.TryGetProperty("values", out var priorValues) &&
            priorValues.TryGetProperty("root_module", out var rootModule))
        {
            ReadPriorState(rootModule, seen, demand);
        }

        var incomplete = demand.Machines.Where(x => x.After is { Incomplete: true } && x.Hypervisor != null).ToList();

        if (incomplete.Count > 0)
            demand.Notes.Add($"{Count(incomplete.Count, "machine")} not wholly sized in the plan (a value known only after apply, or disks taken from a template); what is known of {(incomplete.Count == 1 ? "it" : "them")} is counted.");

        var cloud = demand.Machines.Count(x => x.Hypervisor == null && x.After != null);

        if (cloud > 0)
            demand.Notes.Add($"{Count(cloud, "cloud machine")} counted against the machine limit only: {(cloud == 1 ? "its" : "their")} size is an instance type, and {(cloud == 1 ? "it takes" : "they take")} no room on a hypervisor.");

        return demand;
    }

    /// <summary>Machines the plan leaves alone and does not list, as a targeted plan does.</summary>
    private static void ReadPriorState(JsonElement module, HashSet<string> seen, DeploymentDemand demand)
    {
        foreach (var resource in Array(module, "resources"))
        {
            var address = String(resource, "address");
            var type = String(resource, "type");

            if (String(resource, "mode") != "managed" || address == null || seen.Contains(address) ||
                type == null || !Types.TryGetValue(type, out var machineType))
                continue;

            var values = Object(resource, "values");

            if (values.ValueKind != JsonValueKind.Object)
                continue;

            var size = machineType.Size(values, machineType.Cloned(values));
            seen.Add(address);
            demand.Machines.Add(new PlannedMachine
            {
                Address = address,
                Type = type,
                Hypervisor = machineType.Hypervisor,
                Action = "no-op",
                Host = machineType.Host(values),
                Before = size,
                After = size,
            });
        }

        foreach (var child in Array(module, "child_modules"))
            ReadPriorState(child, seen, demand);
    }

    #region Sizes

    /// <summary>bpg <c>proxmox_virtual_environment_vm</c>: blocks are lists of one object.</summary>
    private static MachineSize BpgVmSize(JsonElement values, bool cloned)
    {
        var cpu = First(values, "cpu");
        var memory = First(values, "memory");
        var size = new MachineSize
        {
            Vcpu = Int(cpu, "cores", 1) * Int(cpu, "sockets", 1),
            MemoryMiB = Int(memory, "dedicated", 512),
        };

        var disks = Array(values, "disk");

        foreach (var disk in disks)
            size.Disks.Add(new PlannedDisk { Datastore = Scalar(disk, "datastore_id") ?? "local-lvm", SizeGiB = Int(disk, "size", 8) });

        // A clone with no disk blocks keeps its template's disks, whose size the plan does not know.
        // Without a clone, bpg gives the VM one 8 GiB disk on local-lvm.
        if (disks.Count == 0)
        {
            if (cloned)
                size.Incomplete = true;
            else
                size.Disks.Add(new PlannedDisk { Datastore = "local-lvm", SizeGiB = 8 });
        }

        return size;
    }

    /// <summary>bpg <c>proxmox_virtual_environment_cloned_vm</c>: cpu and memory are objects, disk a map by slot.</summary>
    private static MachineSize BpgClonedSize(JsonElement values, bool cloned)
    {
        var cpu = ObjectOrFirst(values, "cpu");
        var memory = ObjectOrFirst(values, "memory");
        var size = new MachineSize
        {
            Vcpu = Int(cpu, "cores", 1) * Int(cpu, "sockets", 1),
            MemoryMiB = Int(memory, "size", Int(memory, "dedicated", 512)),
        };

        AddDiskMap(values, "disk", size);
        return size;
    }

    /// <summary>bpg's experimental <c>proxmox_virtual_environment_vm2</c>.</summary>
    private static MachineSize BpgVm2Size(JsonElement values, bool cloned)
    {
        var cpu = ObjectOrFirst(values, "cpu");
        var memory = ObjectOrFirst(values, "memory");
        var size = new MachineSize
        {
            Vcpu = Int(cpu, "cores", 1) * Int(cpu, "sockets", 1),
            MemoryMiB = Int(memory, "size", Int(memory, "dedicated", 512)),
        };

        AddDiskMap(values, "disk", size);
        return size;
    }

    /// <summary>bpg <c>proxmox_virtual_environment_container</c>: 1 core, 512 MiB and a 4 GiB root disk by default.</summary>
    private static MachineSize BpgContainerSize(JsonElement values, bool cloned)
    {
        var cpu = First(values, "cpu");
        var memory = First(values, "memory");
        var size = new MachineSize
        {
            Vcpu = Int(cpu, "cores", 1),
            MemoryMiB = Int(memory, "dedicated", 512),
        };

        var disk = First(values, "disk");

        if (disk.ValueKind == JsonValueKind.Object || !cloned)
            size.Disks.Add(new PlannedDisk { Datastore = Scalar(disk, "datastore_id") ?? "local", SizeGiB = Int(disk, "size", 4) });
        else
            size.Incomplete = true;

        foreach (var mount in Array(values, "mount_point"))
        {
            // A bind mount has no size and takes no space on a datastore.
            if (Gib(Scalar(mount, "size")) is { } gib)
                size.Disks.Add(new PlannedDisk { Datastore = Scalar(mount, "volume"), SizeGiB = gib });
        }

        return size;
    }

    /// <summary>
    /// Telmate <c>proxmox_vm_qemu</c>, 2.x and 3.x: <c>vcpus</c>, else cores
    /// times sockets, at the top level or in a 3.x <c>cpu</c> block; disks in
    /// the <c>disk</c> list, or the 3.x <c>disks</c> tree by bus and slot.
    /// </summary>
    private static MachineSize TelmateQemuSize(JsonElement values, bool cloned)
    {
        var cpu = First(values, "cpu");
        var cores = Int(cpu, "cores", Int(values, "cores", 1));
        var sockets = Int(cpu, "sockets", Int(values, "sockets", 1));
        var vcpus = Int(cpu, "vcores", Int(values, "vcpus", 0));

        var size = new MachineSize
        {
            Vcpu = vcpus > 0 ? vcpus : Math.Max(cores, 1) * Math.Max(sockets, 1),
            MemoryMiB = Int(values, "memory", 512),
        };

        foreach (var disk in Array(values, "disk"))
        {
            var kind = Scalar(disk, "type");

            if (kind is "cdrom" or "cloudinit")
                continue;

            if (Gib(Scalar(disk, "size")) is { } gib)
                size.Disks.Add(new PlannedDisk { Datastore = Scalar(disk, "storage"), SizeGiB = gib });
            else
                size.Incomplete = true;
        }

        foreach (var disks in Array(values, "disks"))
            AddTelmateDisks(disks, size);

        if (size.Disks.Count == 0 && cloned)
            size.Incomplete = true;

        return size;
    }

    /// <summary>Walks the 3.x <c>disks</c> tree, ide/sata/scsi/virtio, then slot, to each <c>disk</c> block.</summary>
    private static void AddTelmateDisks(JsonElement element, MachineSize size)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                AddTelmateDisks(item, size);

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
            return;

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name == "disk")
            {
                foreach (var disk in property.Value.ValueKind == JsonValueKind.Array ? property.Value.EnumerateArray().ToList() : [property.Value])
                {
                    if (Gib(Scalar(disk, "size")) is { } gib)
                        size.Disks.Add(new PlannedDisk { Datastore = Scalar(disk, "storage"), SizeGiB = gib });
                    else
                        size.Incomplete = true;
                }
            }
            else if (property.Name is not ("cdrom" or "cloudinit" or "passthrough" or "ignore"))
            {
                AddTelmateDisks(property.Value, size);
            }
        }
    }

    /// <summary>Telmate <c>proxmox_lxc</c>: a rootfs block and mountpoint blocks, sizes such as "8G".</summary>
    private static MachineSize TelmateLxcSize(JsonElement values, bool cloned)
    {
        var size = new MachineSize
        {
            Vcpu = Math.Max(Int(values, "cores", 1), 1),
            MemoryMiB = Int(values, "memory", 512),
        };

        var rootfs = First(values, "rootfs");

        if (Gib(Scalar(rootfs, "size")) is { } root)
            size.Disks.Add(new PlannedDisk { Datastore = Scalar(rootfs, "storage"), SizeGiB = root });
        else
            size.Incomplete = true;

        foreach (var mount in Array(values, "mountpoint"))
        {
            if (Gib(Scalar(mount, "size")) is { } gib)
                size.Disks.Add(new PlannedDisk { Datastore = Scalar(mount, "storage"), SizeGiB = gib });
        }

        return size;
    }

    /// <summary>
    /// <c>vsphere_virtual_machine</c>: 1 vCPU and 1024 MiB by default; each disk
    /// on its own <c>datastore_id</c>, else the machine's. A machine on a datastore
    /// cluster has its disks placed by Storage DRS, so they go on no datastore here.
    /// </summary>
    private static MachineSize VsphereSize(JsonElement values, bool cloned)
    {
        var size = new MachineSize
        {
            Vcpu = Int(values, "num_cpus", 1),
            MemoryMiB = Int(values, "memory", 1024),
        };

        var machineDatastore = Scalar(values, "datastore_id");

        if (string.IsNullOrEmpty(machineDatastore))
            machineDatastore = null;

        var disks = Array(values, "disk");

        foreach (var disk in disks)
        {
            var datastore = Scalar(disk, "datastore_id");

            // The provider writes "<computed>" or an empty string for a disk on the machine's datastore.
            if (string.IsNullOrEmpty(datastore) || datastore.StartsWith('<'))
                datastore = machineDatastore;

            if (Double(disk, "size") is { } gib)
                size.Disks.Add(new PlannedDisk { Datastore = datastore, SizeGiB = gib });
            else
                size.Incomplete = true;
        }

        if (disks.Count == 0)
            size.Incomplete = true;

        return size;
    }

    /// <summary>A disk map keyed by slot (<c>scsi0</c>), as bpg's newer resources have.</summary>
    private static void AddDiskMap(JsonElement values, string name, MachineSize size)
    {
        if (!values.TryGetProperty(name, out var disks))
        {
            size.Incomplete = true;
            return;
        }

        var entries = disks.ValueKind switch
        {
            JsonValueKind.Object => disks.EnumerateObject().Select(x => x.Value).ToList(),
            JsonValueKind.Array => disks.EnumerateArray().ToList(),
            _ => [],
        };

        if (entries.Count == 0)
            size.Incomplete = true;

        foreach (var disk in entries)
        {
            var gib = Double(disk, "size_gb") ?? Double(disk, "size") ?? Gib(Scalar(disk, "size"));

            if (gib is { } value)
                size.Disks.Add(new PlannedDisk { Datastore = Scalar(disk, "datastore_id"), SizeGiB = value });
            else
                size.Incomplete = true;
        }
    }

    [GeneratedRegex(@"^\s*(\d+(?:\.\d+)?)\s*([KMGTP]?)(?:i?B)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SizePattern();

    /// <summary>
    /// A size in GiB, from a number of GiB or a string such as <c>32G</c>,
    /// <c>512M</c> or <c>1T</c>. A bare number is GiB, as both providers take it.
    /// </summary>
    public static double? Gib(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var match = SizePattern().Match(text);

        if (!match.Success)
            return null;

        var value = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);

        return match.Groups[2].Value.ToUpperInvariant() switch
        {
            "K" => value / (1024 * 1024),
            "M" => value / 1024,
            "T" => value * 1024,
            "P" => value * 1024 * 1024,
            _ => value,
        };
    }

    #endregion

    #region Json

    private static string ReadAction(JsonElement change)
    {
        if (!change.TryGetProperty("actions", out var actions) || actions.ValueKind != JsonValueKind.Array)
            return null;

        var list = actions.EnumerateArray().Select(x => x.GetString()).ToList();

        return list.Count == 2 && list.Contains("create") && list.Contains("delete")
            ? "replace"
            : string.Join(",", list);
    }

    /// <summary>
    /// Whether a size attribute is known only after apply. Only the attributes
    /// sizes are read from count; an id or an address known only after apply does not.
    /// </summary>
    private static bool HasUnknown(JsonElement change) =>
        change.TryGetProperty("after_unknown", out var unknown) &&
        unknown.ValueKind == JsonValueKind.Object &&
        unknown.EnumerateObject().Any(x => Unknown(x.Name, x.Value, inBlock: false));

    private static bool Unknown(string name, JsonElement value, bool inBlock) => value.ValueKind switch
    {
        JsonValueKind.True => SizeAttributes.Contains(name),
        JsonValueKind.Array => (inBlock || BlockAttributes.Contains(name)) &&
            value.EnumerateArray().Any(x => Unknown(name, x, inBlock: true)),
        JsonValueKind.Object => (inBlock || BlockAttributes.Contains(name)) &&
            value.EnumerateObject().Any(x => Unknown(x.Name, x.Value, inBlock: true)),
        _ => false,
    };

    private static readonly HashSet<string> SizeAttributes = new(StringComparer.Ordinal)
    {
        "cores", "sockets", "vcpus", "vcores", "num_cpus", "memory", "dedicated", "size", "size_gb", "cpu", "disk", "disks",
    };

    /// <summary>Blocks whose attributes are looked into: cpu, memory and the disks.</summary>
    private static readonly HashSet<string> BlockAttributes = new(StringComparer.Ordinal)
    {
        "cpu", "memory", "disk", "disks", "rootfs", "mountpoint", "mount_point", "scsi", "sata", "ide", "virtio",
    };

    private static JsonElement Object(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : default;

    private static bool Present(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind is JsonValueKind.Object or JsonValueKind.Array && (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 0);

    private static List<JsonElement> Array(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? [.. value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object)]
            : [];

    private static JsonElement First(JsonElement element, string name) =>
        Array(element, name) is [var first, ..] ? first : default;

    private static JsonElement ObjectOrFirst(JsonElement element, string name) =>
        Object(element, name) is { ValueKind: JsonValueKind.Object } value ? value : First(element, name);

    private static string String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Scalar(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private static double? Double(JsonElement element, string name) =>
        double.TryParse(Scalar(element, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    /// <summary>A whole number, or <paramref name="fallback"/> when it is missing, not a number, or 0 (unset, to Terraform).</summary>
    private static int Int(JsonElement element, string name, int fallback) =>
        Double(element, name) is { } value && value > 0 ? (int)Math.Round(value) : fallback;

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    #endregion
}
