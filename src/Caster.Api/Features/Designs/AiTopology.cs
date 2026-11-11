// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Services.Inventory;
using Caster.Api.Domain.Services.Capacity;
using Caster.Api.Infrastructure.Options;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Caster.Api.Features.Designs;

/// <summary>
/// What a network segment is for in the range. Other comes first, so a client that leaves it unset leaves it open.
/// </summary>
public enum AiSegmentRole
{
    Other,
    Internet,
    Perimeter,
    Dmz,
    UserLan,
    Server,
    Management,
    Ot,
    Attacker
}

/// <summary>
/// What a machine is for in the range.
/// </summary>
public enum AiMachineRole
{
    Attacker,
    ParticipantWorkstation,
    Workstation,
    Server,
    NetworkDevice,
    SecurityTool,
    Ot,
    Other
}

/// <summary>
/// The range an AI draft builds, as the model describes it: its segments, machines and the
/// attacker's path. The server checks it; the files or modules are what is applied.
/// </summary>
public class AiTopologyPlan
{
    public List<AiTopologySegment> Segments { get; set; } = [];
    public List<AiTopologyMachine> Machines { get; set; } = [];
    public List<AiAttackStep> AttackPath { get; set; } = [];

    /// <summary>
    /// The machines' size in all, set by the server.
    /// </summary>
    public AiTopologyTotals Totals { get; set; }
}

public class AiTopologySegment
{
    public string Name { get; set; }
    public AiSegmentRole Role { get; set; }
    public string Cidr { get; set; }
    public int? VlanId { get; set; }

    /// <summary>
    /// The hypervisor network the segment uses or creates: a Proxmox bridge or vnet, or a vSphere port group.
    /// </summary>
    public string Network { get; set; }

    public bool KeyTerrain { get; set; }
    public string Purpose { get; set; }
}

public class AiTopologyMachine
{
    public string Name { get; set; }
    public AiMachineRole Role { get; set; }

    /// <summary>
    /// The participant team a workstation is for.
    /// </summary>
    public string Team { get; set; }

    public string Template { get; set; }
    public int? Cpu { get; set; }
    public int? MemoryMiB { get; set; }
    public int? DiskGiB { get; set; }

    /// <summary>
    /// A critical asset of the scenario: what the attacker is after or the defenders must keep running.
    /// </summary>
    public bool Critical { get; set; }

    public bool KeyTerrain { get; set; }
    public string Purpose { get; set; }
    public List<AiTopologyInterface> Interfaces { get; set; } = [];
}

public class AiTopologyInterface
{
    public string Segment { get; set; }

    /// <summary>
    /// The address on the segment, with or without its prefix length, or dhcp.
    /// </summary>
    public string Address { get; set; }
}

public class AiAttackStep
{
    public string From { get; set; }
    public string To { get; set; }

    /// <summary>
    /// The MITRE ATT&amp;CK technique, e.g. T1566.
    /// </summary>
    public string Technique { get; set; }

    public string Description { get; set; }
}

public class AiTopologyTotals
{
    public int Machines { get; set; }

    /// <summary>
    /// The machines whose size is known, from the plan or their inventory template. The sums count only these.
    /// </summary>
    public int Sized { get; set; }

    public int Cpu { get; set; }
    public long MemoryMiB { get; set; }
    public long DiskGiB { get; set; }

    /// <summary>
    /// Free space on the hypervisor's datastores, when the inventory reports it. For display; the
    /// room is checked in <see cref="Capacity"/>.
    /// </summary>
    public long? DatastoreFreeGiB { get; set; }

    /// <summary>
    /// The machines checked as a run of them would be before apply: against the limits of a
    /// workspace in the draft's project, and the room on each host and datastore.
    /// </summary>
    public CapacityCheck Capacity { get; set; }
}

/// <summary>
/// What a brief expects of the plan, for the checks that compare them.
/// </summary>
public record AiTopologyExpectations(
    IReadOnlyList<string> Segments,
    IReadOnlyList<string> CriticalSystems,
    IReadOnlyList<(string Team, int Workstations)> Workstations,
    string EntryPoint);

/// <summary>
/// The topology guidance both drafts give the model, the plan's tool schema, and the checks the
/// server makes on the plan.
/// </summary>
public static class AiTopology
{
    public const string ToolProperty = "topology";

    public const string NoPlan = "The draft gave no topology plan, so its segments, addresses and attack path were not checked.";

    // more machines than this on one segment, and nothing else, is a flat network
    public const int FlatNetworkMachines = 6;

    public const string Guidance =
@"Range topology:
- Segment the network as a real organization does: a perimeter (firewall or router) between the internet and the inside,
  a DMZ for what the internet reaches, user LANs, server tiers on their own VLANs, and management kept apart.
  Add OT/ICS behind its own boundary only when the scenario has industrial systems.
- Put the attacker outside (on the internet segment or its own) unless the request says insider. The attack path should
  cross the boundaries the defenders are meant to watch, ending at the critical assets.
- Give each participant team its workstations, on a segment of their own or the one they defend.
- Name the key cyber terrain and the critical assets, and put them where the scenario needs them.
- Give each segment its own RFC 1918 block, with no two overlapping, and addresses inside it. Use RFC 5737 addresses
  only for a simulated internet.
- Only routers, firewalls and security tools sit on more than one segment.
- Describe what you build in topology: every segment, machine and attack step, matching the Terraform.";

    public static JsonObject BuildToolSchema()
    {
        static JsonObject Str(string description) => new() { ["type"] = "string", ["description"] = description };
        static JsonObject Int(string description) => new() { ["type"] = "integer", ["description"] = description };
        static JsonObject Bool(string description) => new() { ["type"] = "boolean", ["description"] = description };
        static JsonObject Enum<T>(string description) where T : struct, System.Enum => new()
        {
            ["type"] = "string",
            ["enum"] = new JsonArray(System.Enum.GetNames<T>().Select(n => (JsonNode)JsonValue.Create(n)).ToArray()),
            ["description"] = description
        };

        return new JsonObject
        {
            ["type"] = "object",
            ["description"] = "The range the draft builds: segments, machines and the attack path.",
            ["properties"] = new JsonObject
            {
                ["segments"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["name"] = Str("Segment name, e.g. corp-lan."),
                            ["role"] = Enum<AiSegmentRole>("What the segment is for."),
                            ["cidr"] = Str("IPv4 network, e.g. 10.10.20.0/24."),
                            ["vlanId"] = Int("VLAN ID, 1-4094, when the segment is a VLAN."),
                            ["network"] = Str("The hypervisor network it uses or creates (bridge, vnet or port group)."),
                            ["keyTerrain"] = Bool("True for key cyber terrain."),
                            ["purpose"] = Str("One line on what the segment is for.")
                        },
                        ["required"] = new JsonArray("name", "role")
                    }
                },
                ["machines"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["name"] = Str("Machine name."),
                            ["role"] = Enum<AiMachineRole>("What the machine is for."),
                            ["team"] = Str("The participant team a workstation is for."),
                            ["template"] = Str("The VM template it is cloned from."),
                            ["cpu"] = Int("vCPUs."),
                            ["memoryMiB"] = Int("Memory in MiB."),
                            ["diskGiB"] = Int("Disk in GiB."),
                            ["critical"] = Bool("True for a critical asset."),
                            ["keyTerrain"] = Bool("True for key cyber terrain."),
                            ["purpose"] = Str("One line on what the machine is for."),
                            ["interfaces"] = new JsonObject
                            {
                                ["type"] = "array",
                                ["items"] = new JsonObject
                                {
                                    ["type"] = "object",
                                    ["properties"] = new JsonObject
                                    {
                                        ["segment"] = Str("Segment name."),
                                        ["address"] = Str("IPv4 address on the segment, or dhcp.")
                                    },
                                    ["required"] = new JsonArray("segment")
                                }
                            }
                        },
                        ["required"] = new JsonArray("name", "role", "interfaces")
                    }
                },
                ["attackPath"] = new JsonObject
                {
                    ["type"] = "array",
                    ["description"] = "The attacker's steps in order, from the entry point to the objective.",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["from"] = Str("Machine or segment the step starts from."),
                            ["to"] = Str("Machine or segment the step reaches."),
                            ["technique"] = Str("MITRE ATT&CK technique ID, e.g. T1566."),
                            ["description"] = Str("What happens, as a defender would report it.")
                        },
                        ["required"] = new JsonArray("from", "to")
                    }
                }
            },
            ["required"] = new JsonArray("segments", "machines")
        };
    }

    /// <summary>
    /// The plan in the model's answer, or null when it gave none.
    /// </summary>
    public static AiTopologyPlan Parse(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object || !input.TryGetProperty(ToolProperty, out var t) || t.ValueKind != JsonValueKind.Object)
            return null;

        var plan = new AiTopologyPlan
        {
            Segments = Items(t, "segments").Select(s => new AiTopologySegment
            {
                Name = Str(s, "name"),
                Role = Role(s, AiSegmentRole.Other),
                Cidr = Str(s, "cidr"),
                VlanId = Int(s, "vlanId"),
                Network = Str(s, "network"),
                KeyTerrain = Bool(s, "keyTerrain"),
                Purpose = Str(s, "purpose")
            }).Where(s => !string.IsNullOrWhiteSpace(s.Name)).ToList(),
            Machines = Items(t, "machines").Select(m => new AiTopologyMachine
            {
                Name = Str(m, "name"),
                Role = Role(m, AiMachineRole.Other),
                Team = Str(m, "team"),
                Template = Str(m, "template"),
                Cpu = Int(m, "cpu"),
                MemoryMiB = Int(m, "memoryMiB"),
                DiskGiB = Int(m, "diskGiB"),
                Critical = Bool(m, "critical"),
                KeyTerrain = Bool(m, "keyTerrain"),
                Purpose = Str(m, "purpose"),
                Interfaces = Items(m, "interfaces")
                    .Select(i => new AiTopologyInterface { Segment = Str(i, "segment"), Address = Str(i, "address") })
                    .ToList()
            }).Where(m => !string.IsNullOrWhiteSpace(m.Name)).ToList(),
            AttackPath = Items(t, "attackPath").Select(a => new AiAttackStep
            {
                From = Str(a, "from"),
                To = Str(a, "to"),
                Technique = Str(a, "technique"),
                Description = Str(a, "description")
            }).ToList()
        };
        return plan;
    }

    /// <summary>
    /// The names the plan says must exist on the hypervisor: machine templates and segment networks.
    /// A segment network the files create is left out by the caller.
    /// </summary>
    public static IEnumerable<AiNameUse> NameUses(AiTopologyPlan plan, string hypervisor)
    {
        if (plan == null)
            yield break;
        foreach (var m in plan.Machines.Where(m => IsLiteral(m.Template)))
            yield return new AiNameUse(AiInventoryKind.Template, m.Template, $"topology machine {m.Name}", hypervisor, ByVmId: m.Template.All(char.IsDigit));
        foreach (var s in plan.Segments.Where(s => IsLiteral(s.Network)))
            yield return new AiNameUse(AiInventoryKind.Network, s.Network, $"topology segment {s.Name}", hypervisor);
    }

    /// <summary>
    /// A literal name rather than a reference, an interpolation or a placeholder.
    /// </summary>
    public static bool IsLiteral(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Contains("${")
        && !value.StartsWith(Domain.Models.Placeholders.Prefix, StringComparison.Ordinal)
        && !System.Text.RegularExpressions.Regex.IsMatch(value, @"^(var|module|local|data|each|count)\.");

    /// <summary>
    /// Checks the plan: addressing, segmentation, the attacker's position and path, workstations,
    /// capacity and, with a brief, that what it asks for is there. Sets the plan's totals.
    /// </summary>
    public static List<string> Check(AiTopologyPlan plan, AiInventory inventory, string hypervisor, AiTopologyExpectations expected = null)
    {
        var warnings = new List<string>();
        if (plan == null)
            return warnings;

        warnings.AddRange(AddressProblems(plan));
        warnings.AddRange(SegmentationProblems(plan));
        warnings.AddRange(AttackProblems(plan));
        if (expected != null)
            warnings.AddRange(BriefProblems(plan, expected));
        plan.Totals = Totals(plan, inventory, hypervisor);
        var capacity = plan.Totals.Capacity = CheckCapacity(plan, inventory, hypervisor);
        warnings.AddRange(LimitProblems(capacity));
        warnings.AddRange(capacity.Hosts.Where(h => !h.Fits).Select(h => $"The hypervisors may not have room: {h.Message}"));
        warnings.AddRange(capacity.Datastores.Where(d => !d.Fits).Select(d => $"The hypervisors may not have room: {d.Message}"));
        // only worth saying when there is room or a limit to compare the totals with
        var compared = capacity.Hosts.Count > 0 || capacity.Datastores.Count > 0 || capacity.Limits.Any;
        if (compared && plan.Totals.Sized < plan.Totals.Machines)
            warnings.Add($"{plan.Totals.Machines - plan.Totals.Sized} of {plan.Totals.Machines} machines have no size in the plan or their template, so the totals leave them out.");
        return warnings;
    }

    /// <summary>
    /// The plan's machines as a run that creates them, sized from the plan or their inventory
    /// template. The plan places them on no host, so each is counted against all of its
    /// hypervisor's hosts, and its disk against all of its datastores.
    /// </summary>
    public static DeploymentDemand Demand(AiTopologyPlan plan, AiInventory inventory, string hypervisor)
    {
        var provider = AiInventory.ProviderOf(hypervisor);
        if (provider == null && inventory?.Sources.Select(s => s.Hypervisor).Distinct().ToList() is [var only])
            provider = AiInventory.ProviderOf(only);

        var demand = new DeploymentDemand();
        foreach (var machine in plan?.Machines ?? [])
        {
            var template = inventory?.TemplateSize(hypervisor, machine.Template) ?? default;
            var cpu = machine.Cpu ?? template.Cpu;
            var memory = machine.MemoryMiB ?? template.MemoryMiB;
            var disk = machine.DiskGiB ?? template.DiskGiB;
            demand.Machines.Add(new PlannedMachine
            {
                Address = machine.Name,
                Hypervisor = provider,
                Action = "create",
                After = new MachineSize
                {
                    Vcpu = cpu ?? 0,
                    MemoryMiB = memory ?? 0,
                    Disks = disk is { } gib ? [new PlannedDisk { SizeGiB = gib }] : [],
                    Incomplete = cpu == null || memory == null || disk == null,
                },
            });
        }

        if (provider == null && demand.Machines.Count > 0 && inventory?.Room != null)
            demand.Notes.Add("Room on the hypervisors was not checked: the draft does not say which hypervisor its machines go on.");

        return demand;
    }

    /// <summary>The plan's machines checked against the limits and room the inventory carries.</summary>
    public static CapacityCheck CheckCapacity(AiTopologyPlan plan, AiInventory inventory, string hypervisor) =>
        CapacityChecker.Check(
            Demand(plan, inventory, hypervisor),
            inventory?.Limits ?? new EffectiveLimits(),
            inventory?.Room,
            inventory?.CapacityOptions ?? new CapacityOptions());

    /// <summary>
    /// Refuses to save a draft whose topology plan goes over a limit of a workspace in the
    /// directory's project. Sizes come from the plan or, for a machine it leaves unsized, its
    /// template in the inventory cache; room is not checked here, since it can change before
    /// a run, and the check before apply makes it then.
    /// </summary>
    public static async Task EnsureWithinLimits(
        AiTopologyPlan plan, IInventoryService inventoryService, ICapacityCheckService capacity, Guid directoryId, string what, CancellationToken cancellationToken)
    {
        if (plan == null || capacity == null)
            return;

        var limits = await capacity.GetDirectoryLimitsAsync(directoryId, cancellationToken);
        if (!limits.Any)
            return;

        var inventory = inventoryService == null ? null : AiInventory.Load(inventoryService, null);
        var check = CapacityChecker.Check(Demand(plan, inventory, null), limits, null, capacity.Options ?? new CapacityOptions());
        var problems = LimitProblems(check).ToList();
        if (problems.Count > 0)
            throw new Infrastructure.Exceptions.ConflictException($"This {what} cannot be saved. {string.Join(" ", problems)}");
    }

    /// <summary>The limits the machines go over, in words. A draft with any cannot be saved.</summary>
    public static IEnumerable<string> LimitProblems(CapacityCheck capacity) =>
        (capacity?.LimitViolations ?? []).Select(v =>
        {
            var (one, many) = v.Limit switch
            {
                nameof(EffectiveLimits.MaxMachines) => ("machine", "machines"),
                nameof(EffectiveLimits.MaxVcpu) => ("vCPU", "vCPUs"),
                nameof(EffectiveLimits.MaxMemoryGiB) => ("GiB of memory", "GiB of memory"),
                _ => ("GiB of disk", "GiB of disk"),
            };
            var where = v.Source switch
            {
                LimitSource.Project => "set on the project",
                LimitSource.Workspace => "set on the workspace",
                _ => "set in the Capacity settings",
            };
            string Amount(double x) => $"{x.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} {(x == 1 ? one : many)}";
            return $"The machines need {Amount(v.Total)}, over the limit of {Amount(v.Value)} ({where}) for a workspace. A draft over a limit cannot be saved.";
        });

    private static IEnumerable<string> AddressProblems(AiTopologyPlan plan)
    {
        var networks = new List<(AiTopologySegment Segment, Ipv4Network Network)>();
        foreach (var segment in plan.Segments)
        {
            if (string.IsNullOrWhiteSpace(segment.Cidr))
                continue;
            if (!Ipv4Network.TryParse(segment.Cidr, out var network, out var hostBits))
            {
                yield return $"Segment {segment.Name}: '{segment.Cidr}' is not an IPv4 network such as 10.10.20.0/24.";
                continue;
            }
            if (hostBits)
                yield return $"Segment {segment.Name}: {segment.Cidr} has host bits set; the network is {network}.";
            if (segment.Role != AiSegmentRole.Internet && !network.IsPrivate)
                yield return $"Segment {segment.Name}: {network} is not an RFC 1918 private range. Only a simulated internet segment should use other addresses.";
            networks.Add((segment, network));
        }

        for (var i = 0; i < networks.Count; i++)
            for (var j = i + 1; j < networks.Count; j++)
                if (networks[i].Network.Overlaps(networks[j].Network))
                    yield return $"Segments {networks[i].Segment.Name} ({networks[i].Network}) and {networks[j].Segment.Name} ({networks[j].Network}) overlap.";

        foreach (var vlan in plan.Segments.Where(s => s.VlanId.HasValue))
            if (vlan.VlanId is < 1 or > 4094)
                yield return $"Segment {vlan.Name}: VLAN {vlan.VlanId} is outside 1-4094.";
        foreach (var reused in plan.Segments.Where(s => s.VlanId.HasValue).GroupBy(s => s.VlanId).Where(g => g.Count() > 1))
            yield return $"VLAN {reused.Key} is used by more than one segment ({string.Join(", ", reused.Select(s => s.Name))}).";

        var bySegment = plan.Segments.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var networkOf = networks.ToDictionary(n => n.Segment, n => n.Network);
        var used = new Dictionary<uint, string>();
        foreach (var machine in plan.Machines)
        {
            foreach (var nic in machine.Interfaces)
            {
                if (string.IsNullOrWhiteSpace(nic.Segment) || !bySegment.TryGetValue(nic.Segment, out var segment))
                {
                    yield return $"Machine {machine.Name} is on segment '{nic.Segment}', which the plan does not have.";
                    continue;
                }
                var address = nic.Address?.Trim();
                if (string.IsNullOrEmpty(address) || address.Equals("dhcp", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!Ipv4Network.TryParseAddress(address.Split('/')[0], out var ip))
                {
                    yield return $"Machine {machine.Name}: '{address}' is not an IPv4 address.";
                    continue;
                }
                if (networkOf.TryGetValue(segment, out var net))
                {
                    if (!net.Contains(ip))
                        yield return $"Machine {machine.Name}: {Ipv4Network.Format(ip)} is outside segment {segment.Name} ({net}).";
                    else if (net.PrefixLength <= 30 && (ip == net.Address || ip == net.Broadcast))
                        yield return $"Machine {machine.Name}: {Ipv4Network.Format(ip)} is the {(ip == net.Address ? "network" : "broadcast")} address of {segment.Name}.";
                }
                if (used.TryGetValue(ip, out var other) && other != machine.Name)
                    yield return $"Machines {other} and {machine.Name} both use {Ipv4Network.Format(ip)}.";
                else
                    used[ip] = machine.Name;
            }
        }
    }

    private static IEnumerable<string> SegmentationProblems(AiTopologyPlan plan)
    {
        foreach (var machine in plan.Machines.Where(m => m.Role is not (AiMachineRole.NetworkDevice or AiMachineRole.SecurityTool)))
        {
            var segments = machine.Interfaces.Select(i => i.Segment).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (segments.Count > 1)
                yield return $"Machine {machine.Name} is on {string.Join(" and ", segments)}, which bypasses the boundary between them. Only routers, firewalls and security tools should be.";
        }

        var inside = plan.Segments.Where(s => s.Role is not (AiSegmentRole.Internet or AiSegmentRole.Attacker)).ToList();
        if (inside.Count == 1 && plan.Machines.Count > FlatNetworkMachines)
            yield return $"All {plan.Machines.Count} machines share {inside[0].Name}. A flat network gives the attacker no boundary to cross; consider a DMZ, user and server segments.";
    }

    private static IEnumerable<string> AttackProblems(AiTopologyPlan plan)
    {
        var roleOf = plan.Segments.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Role, StringComparer.OrdinalIgnoreCase);
        var attackers = plan.Machines.Where(m => m.Role == AiMachineRole.Attacker).ToList();
        foreach (var attacker in attackers)
        {
            foreach (var segment in attacker.Interfaces.Select(i => i.Segment).Where(s => s != null).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (roleOf.GetValueOrDefault(segment) == AiSegmentRole.Management)
                    yield return $"Attacker {attacker.Name} is on the management segment {segment}.";
                var exposed = plan.Machines
                    .Where(m => m != attacker && (m.Critical || m.KeyTerrain) && m.Interfaces.Any(i => string.Equals(i.Segment, segment, StringComparison.OrdinalIgnoreCase)))
                    .Select(m => m.Name)
                    .ToList();
                if (exposed.Count > 0)
                    yield return $"Attacker {attacker.Name} shares {segment} with {string.Join(", ", exposed)}, so there is no boundary to cross to reach {(exposed.Count == 1 ? "it" : "them")}.";
            }
        }

        if (attackers.Count > 0 && plan.AttackPath.Count == 0)
            yield return "The plan has an attacker but no attack path.";

        var known = plan.Machines.Select(m => m.Name).Concat(plan.Segments.Select(s => s.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < plan.AttackPath.Count; i++)
        {
            var step = plan.AttackPath[i];
            foreach (var end in new[] { step.From, step.To }.Where(e => !string.IsNullOrWhiteSpace(e) && !known.Contains(e) && !e.Equals("internet", StringComparison.OrdinalIgnoreCase)))
                yield return $"Attack step {i + 1} names '{end}', which is not a machine or segment of the plan.";
        }
    }

    private static IEnumerable<string> BriefProblems(AiTopologyPlan plan, AiTopologyExpectations expected)
    {
        var segments = plan.Segments.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var machines = plan.Machines.Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in expected.Segments ?? [])
            if (!segments.Contains(name))
                yield return $"The brief's segment {name} is not in the plan.";
        foreach (var name in expected.CriticalSystems ?? [])
            if (!machines.Contains(name))
                yield return $"The brief's critical system {name} is not in the plan.";
        foreach (var (team, count) in expected.Workstations ?? [])
        {
            var have = plan.Machines.Count(m => m.Role == AiMachineRole.ParticipantWorkstation && string.Equals(m.Team, team, StringComparison.OrdinalIgnoreCase));
            if (have < count)
                yield return $"Team {team} has {have} of the {count} workstations the brief asks for.";
        }
        if (!string.IsNullOrWhiteSpace(expected.EntryPoint) && !machines.Contains(expected.EntryPoint) && !segments.Contains(expected.EntryPoint)
            && !plan.AttackPath.Any(s => string.Equals(s.From, expected.EntryPoint, StringComparison.OrdinalIgnoreCase) || string.Equals(s.To, expected.EntryPoint, StringComparison.OrdinalIgnoreCase)))
            yield return $"The brief's entry point {expected.EntryPoint} is not in the plan or its attack path.";
    }

    public static AiTopologyTotals Totals(AiTopologyPlan plan, AiInventory inventory, string hypervisor)
    {
        var totals = new AiTopologyTotals { Machines = plan.Machines.Count };
        foreach (var machine in plan.Machines)
        {
            var template = inventory?.TemplateSize(hypervisor, machine.Template) ?? default;
            var cpu = machine.Cpu ?? template.Cpu;
            var memory = machine.MemoryMiB ?? template.MemoryMiB;
            var disk = machine.DiskGiB ?? template.DiskGiB;
            if (cpu == null && memory == null && disk == null)
                continue;
            totals.Sized++;
            totals.Cpu += cpu ?? 0;
            totals.MemoryMiB += memory ?? 0;
            totals.DiskGiB += disk ?? 0;
        }
        if (inventory?.DatastoreSpace(hypervisor) is { } space)
            totals.DatastoreFreeGiB = space.Free / (1024L * 1024 * 1024);
        return totals;
    }

    private static IEnumerable<JsonElement> Items(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object)
            : [];

    private static string Str(JsonElement element, string property) =>
        element.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() : null;

    private static int? Int(JsonElement element, string property) =>
        element.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n
        : element.TryGetProperty(property, out v) && v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out n) ? n
        : null;

    private static bool Bool(JsonElement element, string property) =>
        element.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.True;

    private static T Role<T>(JsonElement element, T fallback) where T : struct, System.Enum =>
        System.Enum.TryParse<T>(Str(element, "role"), ignoreCase: true, out var role) ? role : fallback;
}

/// <summary>
/// An IPv4 network: enough to tell whether two overlap, whether an address is inside, and
/// whether it is an RFC 1918 range.
/// </summary>
public readonly record struct Ipv4Network(uint Address, int PrefixLength)
{
    private uint Mask => PrefixLength == 0 ? 0 : uint.MaxValue << (32 - PrefixLength);
    public uint Broadcast => Address | ~Mask;

    public bool Contains(uint ip) => (ip & Mask) == Address;
    public bool Overlaps(Ipv4Network other) => Contains(other.Address) || other.Contains(Address);

    private static readonly Ipv4Network[] Rfc1918 = [new(0x0A000000, 8), new(0xAC100000, 12), new(0xC0A80000, 16)];
    public bool IsPrivate
    {
        get
        {
            foreach (var range in Rfc1918)
                if (range.Contains(Address) && PrefixLength >= range.PrefixLength)
                    return true;
            return false;
        }
    }

    public override string ToString() => $"{Format(Address)}/{PrefixLength}";

    public static string Format(uint ip) => $"{ip >> 24}.{(ip >> 16) & 255}.{(ip >> 8) & 255}.{ip & 255}";

    public static bool TryParse(string cidr, out Ipv4Network network, out bool hostBits)
    {
        network = default;
        hostBits = false;
        var parts = (cidr ?? "").Trim().Split('/');
        if (parts.Length != 2 || !TryParseAddress(parts[0], out var ip) || !int.TryParse(parts[1], out var prefix) || prefix is < 0 or > 32)
            return false;
        network = new Ipv4Network(ip, prefix);
        network = network with { Address = ip & network.Mask };
        hostBits = network.Address != ip;
        return true;
    }

    public static bool TryParseAddress(string text, out uint ip)
    {
        ip = 0;
        var octets = (text ?? "").Trim().Split('.');
        if (octets.Length != 4)
            return false;
        foreach (var octet in octets)
        {
            if (!byte.TryParse(octet, out var b) || (octet.Length > 1 && octet[0] == '0'))
                return false;
            ip = (ip << 8) | b;
        }
        return true;
    }
}
