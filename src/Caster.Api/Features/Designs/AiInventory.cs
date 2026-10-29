// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.Inventory;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Options;

namespace Caster.Api.Features.Designs;

public enum AiInventoryKind
{
    Template,
    Network,
    Datastore
}

/// <summary>
/// What an AI draft was checked against in the infrastructure inventory, and what it found there.
/// </summary>
public class AiInventoryGrounding
{
    /// <summary>
    /// True when the model was shown the inventory and the draft's names were checked against it.
    /// </summary>
    public bool Checked { get; set; }

    /// <summary>
    /// Why the draft was not checked, or what could not be read; empty when everything was.
    /// </summary>
    public string Reason { get; set; }

    public List<AiInventoryConnection> Connections { get; set; } = [];

    /// <summary>
    /// Names the draft uses that the inventory has.
    /// </summary>
    public List<AiInventoryReference> Found { get; set; } = [];

    /// <summary>
    /// Names the draft uses that the inventory does not have. Each is also a warning.
    /// </summary>
    public List<AiInventoryReference> Unknown { get; set; } = [];
}

/// <summary>
/// An inventory connection a draft was grounded in.
/// </summary>
public class AiInventoryConnection
{
    public string Name { get; set; }
    public string Hypervisor { get; set; }

    /// <summary>
    /// When the inventory was read, in UTC.
    /// </summary>
    public DateTime? LastUpdated { get; set; }

    /// <summary>
    /// True when a category is from an earlier read, because the latest one failed.
    /// </summary>
    public bool Stale { get; set; }

    /// <summary>
    /// The categories that could not be read, so names of that kind were not checked.
    /// </summary>
    public List<AiInventoryKind> Unavailable { get; set; } = [];

    /// <summary>
    /// True when the inventory was too large to show the model in full.
    /// </summary>
    public bool Truncated { get; set; }
}

/// <summary>
/// A template, network or datastore name a draft uses, and where.
/// </summary>
public class AiInventoryReference
{
    public AiInventoryKind Kind { get; set; }
    public string Name { get; set; }

    /// <summary>
    /// Where the draft uses it: a file, a module value or the topology plan.
    /// </summary>
    public string Where { get; set; }

    /// <summary>
    /// The connection the name was found in; empty for an unknown name.
    /// </summary>
    public string Connection { get; set; }
}

/// <summary>
/// A name a draft uses for something that must exist on a hypervisor. Hypervisor is null when the
/// use does not say which; ByVmId is a Proxmox template given by its vmid.
/// </summary>
public record AiNameUse(AiInventoryKind Kind, string Name, string Where, string Hypervisor = null, bool ByVmId = false);

/// <summary>
/// The inventory of the hypervisors a draft targets, as the model is shown it and as the draft is
/// checked against it. Built from the cache; never waits on a provider.
/// </summary>
public class AiInventory
{
    // what the model is shown of the inventory, in all and per category of each connection
    public const int MaxPromptCharacters = 24_000;
    public const int MaxTemplates = 120;
    public const int MaxNetworks = 150;
    public const int MaxDatastores = 40;

    public record Source(string Connection, string Hypervisor, InventorySnapshot Snapshot);

    public IReadOnlyList<Source> Sources { get; }
    public AiInventoryGrounding Grounding { get; }

    private AiInventory(IReadOnlyList<Source> sources, AiInventoryGrounding grounding)
    {
        Sources = sources;
        Grounding = grounding;
    }

    /// <summary>
    /// An inventory with nothing in it, for a draft that is not checked, and why.
    /// </summary>
    public static AiInventory None(string reason) => new([], new AiInventoryGrounding { Checked = false, Reason = reason });

    /// <summary>
    /// The inventory a draft is grounded in for the caller. The inventory routes need ViewHosts, so a
    /// caller without it gets none, rather than its names by way of the model.
    /// </summary>
    public static async Task<AiInventory> ForCaller(ICasterAuthorizationService authorizationService, IInventoryService service, IReadOnlyList<string> hypervisors, CancellationToken cancellationToken)
    {
        if (!await authorizationService.Authorize([SystemPermission.ViewHosts], cancellationToken))
            return None("Not checked against the infrastructure inventory: listing it needs the ViewHosts permission.");
        return Load(service, hypervisors);
    }

    public static string HypervisorOf(string provider) =>
        string.Equals(provider, InfrastructureProviders.Proxmox, StringComparison.OrdinalIgnoreCase) ? Hypervisors.Proxmox
        : string.Equals(provider, InfrastructureProviders.Vsphere, StringComparison.OrdinalIgnoreCase) ? Hypervisors.VSphere
        : null;

    /// <summary>
    /// The enabled connections that read one of the hypervisors, or every enabled one when
    /// hypervisors is null.
    /// </summary>
    public static AiInventory Load(IInventoryService service, IReadOnlyList<string> hypervisors)
    {
        var enabled = (service?.GetConnections() ?? []).Where(c => c.Enabled).ToList();
        if (enabled.Count == 0)
            return None("The infrastructure inventory is not enabled on this server.");

        var sources = enabled
            .Select(c => (Info: c, Hypervisor: HypervisorOf(c.Provider)))
            .Where(c => c.Hypervisor != null && (hypervisors == null || hypervisors.Contains(c.Hypervisor)))
            .Select(c => new Source(c.Info.Name, c.Hypervisor, service.GetSnapshot(c.Info.Name)))
            .Where(s => s.Snapshot != null)
            .ToList();
        if (sources.Count == 0)
            return None($"No infrastructure inventory connection reads {string.Join(" or ", hypervisors ?? [])}.");

        var grounding = new AiInventoryGrounding { Checked = true };
        var problems = new List<string>();
        foreach (var source in sources)
        {
            var connection = new AiInventoryConnection { Name = source.Connection, Hypervisor = source.Hypervisor, LastUpdated = source.Snapshot.LastUpdated };
            foreach (var kind in Enum.GetValues<AiInventoryKind>())
            {
                var category = Category(source.Snapshot, kind);
                connection.Stale |= category.Available && category.Stale;
                if (!category.Available)
                {
                    connection.Unavailable.Add(kind);
                    problems.Add($"{Plural(kind)} of {source.Connection} could not be read: {category.Error}");
                }
            }
            grounding.Connections.Add(connection);
        }

        if (grounding.Connections.All(c => c.Unavailable.Count == Enum.GetValues<AiInventoryKind>().Length))
        {
            grounding.Checked = false;
            grounding.Reason = "The infrastructure inventory could not be read. " + string.Join(" ", problems.Take(1));
            return new AiInventory([], grounding);
        }

        grounding.Reason = problems.Count > 0 ? string.Join(" ", problems) : null;
        return new AiInventory(sources, grounding);
    }

    private static InventoryCategoryResult Category(InventorySnapshot snapshot, AiInventoryKind kind) => kind switch
    {
        AiInventoryKind.Template => snapshot.VmTemplates,
        AiInventoryKind.Network => snapshot.Networks,
        _ => snapshot.Datastores
    } ?? InventoryCategoryResult.Unavailable(InventoryMessages.NotLoaded);

    private static string Plural(AiInventoryKind kind) => kind switch
    {
        AiInventoryKind.Template => "VM templates",
        AiInventoryKind.Network => "Networks",
        _ => "Datastores"
    };

    private static string Label(AiInventoryKind kind) => kind switch
    {
        AiInventoryKind.Template => "template",
        AiInventoryKind.Network => "network",
        _ => "datastore"
    };

    /// <summary>
    /// The inventory as the model is shown it: one block per connection, cut to fit the budget,
    /// with what was cut counted. Empty when the draft is not grounded.
    /// </summary>
    public string BuildPromptSection()
    {
        if (Sources.Count == 0)
            return "";

        var sb = new StringBuilder();
        sb.AppendLine("Infrastructure inventory: what exists on the hypervisor now.");
        sb.AppendLine("- Use these exact names for VM templates, networks and datastores. A name not listed does not exist:");
        sb.AppendLine("  create it in Terraform when the range needs it (a bridge, VLAN or port group), or make it a variable and say so.");
        sb.AppendLine("- Size machines from their template, and keep the range within the free capacity listed.");
        var budget = (MaxPromptCharacters - sb.Length) / Sources.Count;
        foreach (var source in Sources)
        {
            var block = new StringBuilder();
            var read = source.Snapshot.LastUpdated?.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? "never";
            block.AppendLine();
            block.AppendLine($"## {source.Hypervisor} connection \"{source.Connection}\" (read {read})");
            var truncated = false;
            truncated |= AppendCategory(block, source, AiInventoryKind.Template, MaxTemplates, budget);
            truncated |= AppendCategory(block, source, AiInventoryKind.Network, MaxNetworks, budget);
            truncated |= AppendCategory(block, source, AiInventoryKind.Datastore, MaxDatastores, budget);
            if (truncated)
                Grounding.Connections.First(c => c.Name == source.Connection).Truncated = true;
            sb.Append(block);
        }
        return sb.ToString();
    }

    private static bool AppendCategory(StringBuilder block, Source source, AiInventoryKind kind, int max, int budget)
    {
        var category = Category(source.Snapshot, kind);
        if (!category.Available)
        {
            block.AppendLine($"{Plural(kind)} could not be read, so use variables for them.");
            return false;
        }

        var entries = category.Entries ?? [];
        block.AppendLine($"{Plural(kind)}{(category.Stale ? " (from an earlier read, may be out of date)" : "")}:");
        if (entries.Length == 0)
            block.AppendLine("- none");

        var shown = 0;
        foreach (var entry in entries)
        {
            var line = $"- {Describe(kind, entry)}";
            if (shown >= max || block.Length + line.Length > budget)
                break;
            block.AppendLine(line);
            shown++;
        }
        if (shown < entries.Length)
        {
            block.AppendLine($"- and {entries.Length - shown} more not listed");
            return true;
        }
        return false;
    }

    private static string Describe(AiInventoryKind kind, InventoryEntry entry)
    {
        var p = entry.Properties ?? [];
        var details = new List<string>();
        switch (kind)
        {
            case AiInventoryKind.Template:
                Add(details, p, "vmid", v => $"vmid {v}");
                Add(details, p, "node", v => $"node {v}");
                Add(details, p, "maxcpu", v => $"{v} cpu");
                Add(details, p, "cpuCount", v => $"{v} cpu");
                Add(details, p, "maxmem", v => Size(v, 1));
                Add(details, p, "memorySizeMiB", v => Size(v, 1024L * 1024));
                Add(details, p, "maxdisk", v => Size(v, 1) + " disk");
                if (entry.Path != null && entry.Path != entry.Name)
                    details.Add($"path {entry.Path}");
                break;
            case AiInventoryKind.Network:
                Add(details, p, "type", v => v);
                Add(details, p, "zone", v => $"zone {v}");
                Add(details, p, "tag", v => $"VLAN {v}");
                Add(details, p, "cidr", v => v);
                Add(details, p, "alias", v => $"\"{v}\"");
                Add(details, p, "comment", v => $"\"{v}\"");
                break;
            default:
                Add(details, p, "type", v => v);
                if (Bytes(p, "freeSpace") is { } free)
                    details.Add(Bytes(p, "capacity") is { } capacity ? $"{Size(free)} free of {Size(capacity)}" : $"{Size(free)} free");
                break;
        }
        return details.Count > 0 ? $"{entry.Name} ({string.Join(", ", details)})" : entry.Name;
    }

    private static void Add(List<string> details, Dictionary<string, string> properties, string key, Func<string, string> format)
    {
        if (properties.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            details.Add(format(value.Trim()));
    }

    private static string Size(string value, long unit) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Size(n * unit) : value;

    public static string Size(long bytes)
    {
        const double gib = 1024d * 1024 * 1024;
        return bytes >= 1024 * gib
            ? $"{(bytes / (1024 * gib)).ToString("0.#", CultureInfo.InvariantCulture)} TiB"
            : $"{(bytes / gib).ToString("0.#", CultureInfo.InvariantCulture)} GiB";
    }

    private static long? Bytes(Dictionary<string, string> properties, string key) =>
        properties.TryGetValue(key, out var value) && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    /// <summary>
    /// Looks up each name the draft uses, records it as found or unknown, and warns about the
    /// unknown ones. A name is only checked against connections whose category was read.
    /// </summary>
    public void Check(IEnumerable<AiNameUse> uses, List<string> warnings)
    {
        if (Sources.Count == 0)
            return;

        foreach (var group in uses
            .Where(u => !string.IsNullOrWhiteSpace(u.Name))
            .GroupBy(u => (u.Kind, Name: u.Name.Trim(), u.Hypervisor, u.ByVmId)))
        {
            var (kind, name, hypervisor, byVmId) = group.Key;
            var where = string.Join(", ", group.Select(u => u.Where).Distinct());
            var sources = Sources
                .Where(s => (hypervisor == null || s.Hypervisor == hypervisor) && Category(s.Snapshot, kind).Available)
                .ToList();
            if (sources.Count == 0)
                continue;

            var found = sources
                .Select(s => (Source: s, Entry: Find(s, kind, name, byVmId, StringComparison.Ordinal)))
                .FirstOrDefault(x => x.Entry != null);
            if (found.Entry != null)
            {
                Grounding.Found.Add(new AiInventoryReference { Kind = kind, Name = name, Where = where, Connection = found.Source.Connection });
                continue;
            }

            Grounding.Unknown.Add(new AiInventoryReference { Kind = kind, Name = name, Where = where });
            var inventory = hypervisor ?? string.Join(" or ", sources.Select(s => s.Hypervisor).Distinct());
            var near = sources.Select(s => Find(s, kind, name, byVmId, StringComparison.OrdinalIgnoreCase)).FirstOrDefault(e => e != null);
            var what = byVmId ? $"{Label(kind)} vmid {name}" : $"{Label(kind)} '{name}'";
            warnings.Add(near != null
                ? $"{where}: {what} is not in the {inventory} inventory, which has '{near.Name}'. Names are case sensitive."
                : $"{where}: {what} is not in the {inventory} inventory. Use one that is listed, create it, or make it a variable.");
        }
    }

    private static InventoryEntry Find(Source source, AiInventoryKind kind, string name, bool byVmId, StringComparison comparison)
    {
        var entries = Category(source.Snapshot, kind).Entries ?? [];
        if (byVmId)
            return entries.FirstOrDefault(e => (e.Properties ?? []).TryGetValue("vmid", out var id) && id == name);

        return entries.FirstOrDefault(e =>
            string.Equals(e.Name, name, comparison)
            || string.Equals(e.Path, name, comparison)
            || (e.Path ?? "").EndsWith("/" + name, comparison));
    }

    /// <summary>
    /// A template's cpu count, memory in MiB and disk in GiB, as the inventory reports them.
    /// </summary>
    public (int? Cpu, int? MemoryMiB, int? DiskGiB) TemplateSize(string hypervisor, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return default;

        var byVmId = name.All(char.IsDigit);
        var entry = Sources
            .Where(s => (hypervisor == null || s.Hypervisor == hypervisor) && s.Snapshot.VmTemplates?.Available == true)
            .Select(s => Find(s, AiInventoryKind.Template, name.Trim(), byVmId, StringComparison.Ordinal))
            .FirstOrDefault(e => e != null);
        if (entry == null)
            return default;

        var p = entry.Properties ?? [];
        var cpu = Number(p, "maxcpu") ?? Number(p, "cpuCount");
        var memory = Number(p, "maxmem") is { } mem ? mem / (1024 * 1024) : Number(p, "memorySizeMiB");
        var disk = Number(p, "maxdisk") is { } d ? d / (1024L * 1024 * 1024) : (long?)null;
        return ((int?)cpu, (int?)memory, (int?)disk);
    }

    private static long? Number(Dictionary<string, string> properties, string key) =>
        properties.TryGetValue(key, out var value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? (long)n : null;

    /// <summary>
    /// The free space and capacity of the datastores of the hypervisor's connections, in bytes,
    /// counting only datastores that report them. Null when none does.
    /// </summary>
    public (long Free, long Capacity)? DatastoreSpace(string hypervisor)
    {
        var sized = Sources
            .Where(s => (hypervisor == null || s.Hypervisor == hypervisor) && s.Snapshot.Datastores?.Available == true)
            .SelectMany(s => s.Snapshot.Datastores.Entries ?? [])
            .Select(e => (Free: Bytes(e.Properties ?? [], "freeSpace"), Capacity: Bytes(e.Properties ?? [], "capacity")))
            .Where(x => x.Free.HasValue)
            .ToList();
        return sized.Count == 0 ? null : (sized.Sum(x => x.Free.Value), sized.Sum(x => x.Capacity ?? x.Free.Value));
    }
}
