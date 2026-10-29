// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.Inventory;
using Caster.Api.Features.Designs;
using Caster.Api.Infrastructure.Options;

namespace Caster.Api.Tests.Features.Designs;

/// <summary>
/// <c>AiTopology</c> - the plan the model gives with a draft, and the checks the server makes on it:
/// addressing, segmentation, the attacker's position and path, what a brief asks for, and capacity.
/// </summary>
[Trait("Category", "Unit")]
public class AiTopologyTests
{
    private static AiTopologySegment Segment(string name, AiSegmentRole role, string cidr = null, int? vlan = null) =>
        new() { Name = name, Role = role, Cidr = cidr, VlanId = vlan };

    private static AiTopologyMachine Machine(string name, AiMachineRole role, params (string Segment, string Address)[] nics) => new()
    {
        Name = name,
        Role = role,
        Interfaces = nics.Select(n => new AiTopologyInterface { Segment = n.Segment, Address = n.Address }).ToList()
    };

    // a perimeter, DMZ and LAN behind a firewall, with an attacker outside: nothing to warn about
    private static AiTopologyPlan Segmented() => new()
    {
        Segments =
        [
            Segment("internet", AiSegmentRole.Internet, "203.0.113.0/24"),
            Segment("dmz", AiSegmentRole.Dmz, "10.0.10.0/24", 10),
            Segment("corp", AiSegmentRole.UserLan, "10.0.20.0/24", 20),
        ],
        Machines =
        [
            Machine("fw", AiMachineRole.NetworkDevice, ("internet", "203.0.113.1"), ("dmz", "10.0.10.1"), ("corp", "10.0.20.1")),
            Machine("kali", AiMachineRole.Attacker, ("internet", "203.0.113.50")),
            Machine("web", AiMachineRole.Server, ("dmz", "10.0.10.10")),
            Machine("dc", AiMachineRole.Server, ("corp", "10.0.20.10")),
        ],
        AttackPath = [new AiAttackStep { From = "kali", To = "web", Technique = "T1190" }, new AiAttackStep { From = "web", To = "dc" }]
    };

    private static List<string> Check(AiTopologyPlan plan, AiTopologyExpectations expected = null) =>
        AiTopology.Check(plan, inventory: null, hypervisor: null, expected);

    [Fact]
    public void A_segmented_plan_has_no_problems()
    {
        Assert.Empty(Check(Segmented()));
    }

    [Fact]
    public void Check_has_nothing_to_say_about_a_missing_plan()
    {
        Assert.Empty(AiTopology.Check(null, null, null));
    }

    [Fact]
    public void Overlapping_segments_are_a_problem()
    {
        var plan = Segmented();
        plan.Segments.Add(Segment("servers", AiSegmentRole.Server, "10.0.0.0/16"));

        Assert.Contains(Check(plan), w => w.Contains("dmz (10.0.10.0/24) and servers (10.0.0.0/16) overlap"));
    }

    [Fact]
    public void A_public_range_is_only_for_the_internet_segment()
    {
        var plan = Segmented();
        plan.Segments[1].Cidr = "8.8.0.0/16";

        var warnings = Check(plan);

        Assert.Contains(warnings, w => w.StartsWith("Segment dmz: 8.8.0.0/16 is not an RFC 1918"));
        Assert.DoesNotContain(warnings, w => w.StartsWith("Segment internet"));
    }

    [Theory]
    [InlineData("10.0.10.5/24", "has host bits set; the network is 10.0.10.0/24")]
    [InlineData("10.0.10.0", "is not an IPv4 network")]
    [InlineData("10.0.300.0/24", "is not an IPv4 network")]
    public void A_malformed_cidr_is_a_problem(string cidr, string expected)
    {
        var plan = Segmented();
        plan.Segments[1].Cidr = cidr;

        Assert.Contains(Check(plan), w => w.Contains(expected));
    }

    [Fact]
    public void A_reused_or_impossible_VLAN_is_a_problem()
    {
        var plan = Segmented();
        plan.Segments[2].VlanId = 10;
        plan.Segments.Add(Segment("ot", AiSegmentRole.Ot, "10.0.30.0/24", 5000));

        var warnings = Check(plan);

        Assert.Contains("VLAN 10 is used by more than one segment (dmz, corp).", warnings);
        Assert.Contains("Segment ot: VLAN 5000 is outside 1-4094.", warnings);
    }

    [Fact]
    public void Addresses_must_be_inside_their_segment_and_unique()
    {
        var plan = Segmented();
        plan.Machines.Add(Machine("ws1", AiMachineRole.Workstation, ("corp", "10.0.21.5")));
        plan.Machines.Add(Machine("ws2", AiMachineRole.Workstation, ("corp", "10.0.20.10/24")));
        plan.Machines.Add(Machine("ws3", AiMachineRole.Workstation, ("corp", "10.0.20.255")));
        plan.Machines.Add(Machine("ws4", AiMachineRole.Workstation, ("corp", "dhcp")));
        plan.Machines.Add(Machine("ws5", AiMachineRole.Workstation, ("guest", "10.9.9.9")));

        var warnings = Check(plan);

        Assert.Contains("Machine ws1: 10.0.21.5 is outside segment corp (10.0.20.0/24).", warnings);
        Assert.Contains("Machines dc and ws2 both use 10.0.20.10.", warnings);
        Assert.Contains("Machine ws3: 10.0.20.255 is the broadcast address of corp.", warnings);
        Assert.Contains("Machine ws5 is on segment 'guest', which the plan does not have.", warnings);
        Assert.DoesNotContain(warnings, w => w.Contains("ws4"));
    }

    [Fact]
    public void Only_routers_firewalls_and_security_tools_bridge_segments()
    {
        var plan = Segmented();
        plan.Machines.Add(Machine("jump", AiMachineRole.Server, ("dmz", "10.0.10.20"), ("corp", "10.0.20.20")));
        plan.Machines.Add(Machine("ids", AiMachineRole.SecurityTool, ("dmz", "10.0.10.30"), ("corp", "10.0.20.30")));

        var warnings = Check(plan);

        Assert.Contains(warnings, w => w.StartsWith("Machine jump is on dmz and corp"));
        Assert.DoesNotContain(warnings, w => w.Contains("ids") || w.Contains("Machine fw"));
    }

    [Fact]
    public void A_flat_network_is_a_problem()
    {
        var plan = new AiTopologyPlan
        {
            Segments = [Segment("lan", AiSegmentRole.UserLan, "10.1.0.0/24")],
            Machines = Enumerable.Range(1, AiTopology.FlatNetworkMachines + 1).Select(i => Machine($"m{i}", AiMachineRole.Workstation, ("lan", $"10.1.0.{i + 10}"))).ToList()
        };

        Assert.Contains(Check(plan), w => w.StartsWith("All 7 machines share lan"));
    }

    [Fact]
    public void An_attacker_beside_a_critical_asset_or_on_management_is_a_problem()
    {
        var plan = Segmented();
        plan.Segments.Add(Segment("mgmt", AiSegmentRole.Management, "10.0.99.0/24"));
        plan.Machines[3].Critical = true;
        plan.Machines.Add(Machine("insider", AiMachineRole.Attacker, ("corp", "10.0.20.66")));
        plan.Machines.Add(Machine("rogue", AiMachineRole.Attacker, ("mgmt", "10.0.99.66")));

        var warnings = Check(plan);

        Assert.Contains("Attacker insider shares corp with dc, so there is no boundary to cross to reach it.", warnings);
        Assert.Contains("Attacker rogue is on the management segment mgmt.", warnings);
    }

    [Fact]
    public void An_attacker_needs_a_path_and_each_step_names_the_plan()
    {
        var plan = Segmented();
        plan.AttackPath = [];
        Assert.Contains("The plan has an attacker but no attack path.", Check(plan));

        plan.AttackPath = [new AiAttackStep { From = "Internet", To = "mail" }];
        var warnings = Check(plan);
        Assert.Contains("Attack step 1 names 'mail', which is not a machine or segment of the plan.", warnings);
        Assert.DoesNotContain(warnings, w => w.Contains("'Internet'"));
    }

    [Fact]
    public void A_brief_expects_its_segments_critical_systems_workstations_and_entry_point()
    {
        var plan = Segmented();
        plan.Machines.Add(new AiTopologyMachine { Name = "blue-1", Role = AiMachineRole.ParticipantWorkstation, Team = "Blue", Interfaces = [new() { Segment = "corp" }] });
        var expected = new AiTopologyExpectations(["dmz", "SCADA"], ["dc", "historian"], [("Blue", 2), ("Red", 1)], "vpn");

        var warnings = Check(plan, expected);

        Assert.Contains("The brief's segment SCADA is not in the plan.", warnings);
        Assert.Contains("The brief's critical system historian is not in the plan.", warnings);
        Assert.Contains("Team Blue has 1 of the 2 workstations the brief asks for.", warnings);
        Assert.Contains("Team Red has 0 of the 1 workstations the brief asks for.", warnings);
        Assert.Contains("The brief's entry point vpn is not in the plan or its attack path.", warnings);
        Assert.DoesNotContain(warnings, w => w.Contains("segment dmz") || w.Contains("system dc"));
    }

    [Fact]
    public void Totals_take_sizes_from_the_template_and_warn_past_the_free_space()
    {
        var plan = Segmented();
        foreach (var machine in plan.Machines)
            machine.Template = "ubuntu-22";
        plan.Machines[0].DiskGiB = 40;
        var inventory = AiInventory.Load(AiInventoryTests.Service(("default", InfrastructureProviders.Proxmox, AiInventoryTests.Snapshot(
            [AiInventoryTests.Entry("ubuntu-22", ("maxcpu", "2"), ("maxmem", "4294967296"), ("maxdisk", "34359738368"))],
            datastores: [AiInventoryTests.Entry("local", ("freeSpace", "107374182400"))]))), null);

        var warnings = AiTopology.Check(plan, inventory, Hypervisors.Proxmox);

        Assert.Equal(4, plan.Totals.Sized);
        Assert.Equal(8, plan.Totals.Cpu);
        Assert.Equal(16384, plan.Totals.MemoryMiB);
        Assert.Equal(40 + 3 * 32, plan.Totals.DiskGiB);
        Assert.Equal(100, plan.Totals.DatastoreFreeGiB);
        Assert.Contains("The machines need 136 GiB of disk, more than the 100 GiB free on the Proxmox datastores.", warnings);
    }

    [Fact]
    public void Totals_say_how_many_machines_have_no_size_when_there_is_capacity_to_compare()
    {
        var plan = Segmented();
        plan.Machines[0].DiskGiB = 10;
        var inventory = AiInventory.Load(AiInventoryTests.Service(("default", InfrastructureProviders.Proxmox, AiInventoryTests.Snapshot(
            datastores: [AiInventoryTests.Entry("local", ("freeSpace", "107374182400"))]))), null);

        Assert.Contains("3 of 4 machines have no size in the plan or their template, so the totals leave them out.",
            AiTopology.Check(plan, inventory, Hypervisors.Proxmox));
        Assert.DoesNotContain(Check(Segmented()), w => w.Contains("no size"));
    }

    [Fact]
    public void Parse_reads_the_plan_from_the_answer()
    {
        using var doc = JsonDocument.Parse("""
            {"topology":{
              "segments":[{"name":"corp","role":"userlan","cidr":"10.0.20.0/24","vlanId":20,"network":"vnet20","keyTerrain":true}],
              "machines":[{"name":"dc","role":"Server","template":"win2022","cpu":"4","memoryMiB":8192,"critical":true,
                           "interfaces":[{"segment":"corp","address":"10.0.20.10"}]},{"role":"Server"}],
              "attackPath":[{"from":"kali","to":"dc","technique":"T1021"}]}}
            """);

        var plan = AiTopology.Parse(doc.RootElement);

        var segment = Assert.Single(plan.Segments);
        Assert.Equal(AiSegmentRole.UserLan, segment.Role);
        Assert.Equal(20, segment.VlanId);
        Assert.True(segment.KeyTerrain);
        var machine = Assert.Single(plan.Machines);
        Assert.Equal(4, machine.Cpu);
        Assert.True(machine.Critical);
        Assert.Equal("10.0.20.10", Assert.Single(machine.Interfaces).Address);
        Assert.Equal("T1021", Assert.Single(plan.AttackPath).Technique);
    }

    [Fact]
    public void Parse_is_null_without_a_plan()
    {
        using var doc = JsonDocument.Parse("""{"summary":"s"}""");

        Assert.Null(AiTopology.Parse(doc.RootElement));
    }

    [Fact]
    public void NameUses_are_the_literal_templates_and_networks()
    {
        var plan = new AiTopologyPlan
        {
            Segments = [new() { Name = "a", Network = "vnet10" }, new() { Name = "b", Network = "${var.lan}" }],
            Machines = [new() { Name = "m", Template = "9000" }, new() { Name = "n", Template = Placeholders.For("template") }]
        };

        var uses = AiTopology.NameUses(plan, Hypervisors.Proxmox).ToList();

        Assert.Equal(2, uses.Count);
        Assert.Contains(uses, u => u.Kind == AiInventoryKind.Template && u.Name == "9000" && u.ByVmId);
        Assert.Contains(uses, u => u.Kind == AiInventoryKind.Network && u.Name == "vnet10" && u.Where == "topology segment a");
    }

    [Theory]
    [InlineData("10.0.0.0/8", true)]
    [InlineData("10.20.0.0/16", true)]
    [InlineData("172.16.0.0/12", true)]
    [InlineData("172.32.0.0/16", false)]
    [InlineData("192.168.10.0/24", true)]
    [InlineData("192.0.0.0/8", false)]
    [InlineData("100.64.0.0/10", false)]
    public void Ipv4Network_tells_an_RFC_1918_range(string cidr, bool expected)
    {
        Assert.True(Ipv4Network.TryParse(cidr, out var network, out _));

        Assert.Equal(expected, network.IsPrivate);
    }

    [Fact]
    public void The_tool_schema_offers_every_role()
    {
        var schema = AiTopology.BuildToolSchema().ToJsonString();

        Assert.Contains("ParticipantWorkstation", schema);
        Assert.Contains("\"Ot\"", schema);
        Assert.Contains("attackPath", schema);
    }
}
