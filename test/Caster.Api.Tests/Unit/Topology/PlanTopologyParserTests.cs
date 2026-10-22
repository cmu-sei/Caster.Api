// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.IO;
using System.Linq;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.Topology;
using Xunit;
using File = System.IO.File;

namespace Caster.Api.Tests.Unit.Topology
{
    /// <summary>
    /// Runs against two captured plans: a Proxmox composition of the network,
    /// router and machine modules (Terraform 1.15, every NIC known at plan
    /// time), and the vSphere plan in Data/plan.json (Terraform 0.12, most NICs
    /// on a port group created in the same plan, so known only by reference).
    /// </summary>
    [Trait("Category", "Unit")]
    [Trait("Category", "Topology")]
    public class PlanTopologyParserTests
    {
        private static readonly string Data = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");

        private static Caster.Api.Domain.Models.Topology Proxmox() =>
            PlanTopologyParser.Parse(File.ReadAllText(Path.Combine(Data, "topology", "proxmox-modules.plan.json")));

        private static Caster.Api.Domain.Models.Topology Vsphere() =>
            PlanTopologyParser.Parse(File.ReadAllText(Path.Combine(Data, "plan.json")));

        private static string[] NetworksOf(Caster.Api.Domain.Models.Topology topology, string machine) =>
            [.. topology.Edges.Where(x => x.From == machine).OrderBy(x => x.Nic).Select(x => x.To)];

        #region Proxmox modules

        [Fact]
        public void Proxmox_DrawsEveryVmAndVnetAndNothingElse()
        {
            var topology = Proxmox();

            Assert.Equal(9, topology.Nodes.Count(x => x.Kind == TopologyNodeKind.Machine));
            Assert.Equal(4, topology.Nodes.Count(x => x.Kind == TopologyNodeKind.Network));
            // The SDN appliers and the template data source are plumbing.
            Assert.DoesNotContain(topology.Nodes, x => x.Type == "proxmox_sdn_applier");
            Assert.DoesNotContain(topology.Nodes, x => x.Type == "proxmox_virtual_environment_vms");
        }

        [Fact]
        public void Proxmox_RouterAttachesToEverySegmentInOrder()
        {
            var topology = Proxmox();

            Assert.Equal(
                [
                    "module.network.proxmox_sdn_vnet.segment[\"outside\"]",
                    "module.network.proxmox_sdn_vnet.segment[\"dmz\"]",
                    "module.network.proxmox_sdn_vnet.segment[\"corp\"]",
                    "module.network.proxmox_sdn_vnet.segment[\"mgmt\"]",
                ],
                NetworksOf(topology, "module.fw_edge.proxmox_virtual_environment_vm.vm"));
        }

        [Fact]
        public void Proxmox_ResolvesNicsThroughModulesAndForEach()
        {
            // The NIC's own references are only var.network_name; the root
            // passes module.network.network_names[each.value.segment]. Only the
            // planned value says which vnet it is.
            var topology = Proxmox();

            Assert.Equal(
                ["module.network.proxmox_sdn_vnet.segment[\"corp\"]"],
                NetworksOf(topology, "module.host[\"dc\"].proxmox_virtual_environment_vm.vm"));
            Assert.Equal(
                ["module.network.proxmox_sdn_vnet.segment[\"outside\"]"],
                NetworksOf(topology, "module.host[\"kali-red\"].proxmox_virtual_environment_vm.vm"));
        }

        [Fact]
        public void Proxmox_LabelsAndPropertiesComeFromPlannedValues()
        {
            var topology = Proxmox();

            var dc = topology.Nodes.Single(x => x.Id == "module.host[\"dc\"].proxmox_virtual_environment_vm.vm");
            Assert.Equal("dc-demo", dc.Label);
            Assert.Equal("module.host[\"dc\"]", dc.Module);
            Assert.Equal("create", dc.Action);
            Assert.Equal("1", dc.Properties["cpus"]);
            Assert.Equal("512", dc.Properties["memory_mb"]);
            Assert.Equal("pve", dc.Properties["node"]);

            var corp = topology.Nodes.Single(x => x.Id == "module.network.proxmox_sdn_vnet.segment[\"corp\"]");
            Assert.Equal("demo-corp", corp.Label);
        }

        [Fact]
        public void Proxmox_SimpleZoneVnetsHaveNoVlan()
        {
            Assert.All(
                Proxmox().Nodes.Where(x => x.Kind == TopologyNodeKind.Network),
                x => Assert.False(x.Properties.ContainsKey("vlan")));
        }

        [Fact]
        public void Proxmox_HasNoExternalNetworks()
        {
            Assert.DoesNotContain(Proxmox().Nodes, x => x.External);
        }

        #endregion

        #region vSphere

        [Fact]
        public void Vsphere_UnknownNetworkIdResolvesByReferenceToThePortGroup()
        {
            // network_id is unknown until apply: the data source reads a port
            // group this plan creates. The data source is folded into it.
            var topology = Vsphere();

            Assert.Equal(
                ["vsphere_host_port_group.course"],
                NetworksOf(topology, "vsphere_virtual_machine.course-centos7-server"));
            Assert.DoesNotContain(topology.Nodes, x => x.Id == "data.vsphere_network.course");
        }

        [Fact]
        public void Vsphere_KnownNetworkIdResolvesByValueToAnExistingNetwork()
        {
            var topology = Vsphere();

            Assert.Equal(
                ["vsphere_host_port_group.course", "data.vsphere_network.course-net"],
                NetworksOf(topology, "vsphere_virtual_machine.course-centos6-student"));

            var existing = topology.Nodes.Single(x => x.Id == "data.vsphere_network.course-net");
            Assert.Equal("course-net", existing.Label);
            Assert.Equal("no-op", existing.Action);
        }

        [Fact]
        public void Vsphere_UntaggedPortGroupHasNoVlan()
        {
            // vlan_id 0 is no VLAN.
            var course = Vsphere().Nodes.Single(x => x.Id == "vsphere_host_port_group.course");

            Assert.False(course.Properties.ContainsKey("vlan"));
        }

        [Fact]
        public void Vsphere_EveryMachineIsAttached()
        {
            var topology = Vsphere();

            var machines = topology.Nodes.Where(x => x.Kind == TopologyNodeKind.Machine).ToList();

            Assert.Equal(7, machines.Count);
            Assert.All(machines, m => Assert.Contains(topology.Edges, e => e.From == m.Id));
            Assert.All(machines, m => Assert.Equal("1024", m.Properties["memory_mb"]));
        }

        #endregion

        #region Hand-written edge cases

        private static string Plan(string resourceChanges, string configuration = "{}") =>
            $$$$"""{"format_version":"1.2","resource_changes":[{{{{resourceChanges}}}}],"configuration":{"root_module":{{{{configuration}}}}}}""";

        private static string Vm(string address, string actions, string bridges, string valuesKey = "after") =>
            $$$$"""
            {"address":"{{{{address}}}}","mode":"managed","type":"proxmox_virtual_environment_vm","name":"vm",
             "change":{"actions":[{{{{actions}}}}],"{{{{valuesKey}}}}":{"name":"{{{{address}}}}","network_device":[{{{{bridges}}}}]}}}
            """;

        [Fact]
        public void BridgeWithNoNetworkInThePlan_BecomesAnExternalNetwork()
        {
            var topology = PlanTopologyParser.Parse(Plan(Vm("proxmox_virtual_environment_vm.a", "\"create\"", """{"bridge":"vmbr0"}""")));

            var external = Assert.Single(topology.Nodes, x => x.External);
            Assert.Equal("external:vmbr0", external.Id);
            Assert.Equal("vmbr0", external.Label);
            Assert.Equal(["external:vmbr0"], NetworksOf(topology, "proxmox_virtual_environment_vm.a"));
        }

        [Fact]
        public void TwoMachinesOnTheSameExternalBridge_ShareOneNode()
        {
            var topology = PlanTopologyParser.Parse(Plan(
                Vm("proxmox_virtual_environment_vm.a", "\"create\"", """{"bridge":"vmbr0"}""") + "," +
                Vm("proxmox_virtual_environment_vm.b", "\"create\"", """{"bridge":"vmbr0"}""")));

            Assert.Single(topology.Nodes, x => x.External);
            Assert.Equal(2, topology.Edges.Count);
        }

        [Fact]
        public void VlanTagsOnOneBridge_AreSeparateNetworks()
        {
            var topology = PlanTopologyParser.Parse(Plan(
                Vm("proxmox_virtual_environment_vm.a", "\"create\"", """{"bridge":"vmbr0","vlan_id":10}""") + "," +
                Vm("proxmox_virtual_environment_vm.b", "\"create\"", """{"bridge":"vmbr0","vlan_id":20}""") + "," +
                Vm("proxmox_virtual_environment_vm.c", "\"create\"", """{"bridge":"vmbr0","vlan_id":0}""")));

            Assert.Equal(["external:vmbr0.10"], NetworksOf(topology, "proxmox_virtual_environment_vm.a"));
            Assert.Equal(["external:vmbr0.20"], NetworksOf(topology, "proxmox_virtual_environment_vm.b"));
            // 0 is untagged: the bridge itself.
            Assert.Equal(["external:vmbr0"], NetworksOf(topology, "proxmox_virtual_environment_vm.c"));

            var tagged = topology.Nodes.Single(x => x.Id == "external:vmbr0.10");
            Assert.Equal("vmbr0.10", tagged.Label);
            Assert.Equal("10", tagged.Properties["vlan"]);
            Assert.False(topology.Nodes.Single(x => x.Id == "external:vmbr0").Properties.ContainsKey("vlan"));
        }

        [Fact]
        public void VnetTag_IsTheNetworksVlan()
        {
            var topology = PlanTopologyParser.Parse(Plan(
                Vm("proxmox_virtual_environment_vm.a", "\"create\"", """{"bridge":"lan"}""") + "," + """
                {"address":"proxmox_sdn_vnet.lan","mode":"managed","type":"proxmox_sdn_vnet","name":"lan",
                 "change":{"actions":["create"],"after":{"id":"lan","alias":"corp","zone":"vlanzone","tag":30}}}
                """));

            var lan = topology.Nodes.Single(x => x.Id == "proxmox_sdn_vnet.lan");
            Assert.Equal("30", lan.Properties["vlan"]);
            Assert.Equal(["proxmox_sdn_vnet.lan"], NetworksOf(topology, "proxmox_virtual_environment_vm.a"));
        }

        [Fact]
        public void DestroyPlan_UsesTheBeforeValues()
        {
            var topology = PlanTopologyParser.Parse(Plan(
                Vm("proxmox_virtual_environment_vm.a", "\"delete\"", """{"bridge":"vmbr0"}""", valuesKey: "before")));

            var vm = topology.Nodes.Single(x => x.Kind == TopologyNodeKind.Machine);
            Assert.Equal("delete", vm.Action);
            Assert.Single(topology.Edges);
        }

        [Fact]
        public void DeleteThenCreate_IsAReplace()
        {
            var topology = PlanTopologyParser.Parse(Plan(
                Vm("proxmox_virtual_environment_vm.a", "\"delete\",\"create\"", """{"bridge":"vmbr0"}""")));

            Assert.Equal("replace", topology.Nodes.Single(x => x.Kind == TopologyNodeKind.Machine).Action);
        }

        [Fact]
        public void UnknownBridgeWithAnAmbiguousReference_DrawsNoEdge()
        {
            // proxmox_sdn_vnet.lan has two instances and the NIC references the
            // block, not one of them; guessing would draw a wrong wire.
            var vnets = string.Join(",", new[] { 0, 1 }.Select(i => $$$$"""
                {"address":"proxmox_sdn_vnet.lan[{{{{i}}}}]","mode":"managed","type":"proxmox_sdn_vnet","name":"lan",
                 "change":{"actions":["create"],"after":{"zone":"z"}}}
                """));
            var configuration = """
                {"resources":[{"address":"proxmox_virtual_environment_vm.a","expressions":
                  {"network_device":[{"bridge":{"references":["proxmox_sdn_vnet.lan"]}}]}}]}
                """;

            var topology = PlanTopologyParser.Parse(Plan(
                Vm("proxmox_virtual_environment_vm.a", "\"create\"", "{}") + "," + vnets,
                configuration));

            Assert.Empty(topology.Edges);
            Assert.Equal(2, topology.Nodes.Count(x => x.Kind == TopologyNodeKind.Network));
        }

        [Fact]
        public void UnknownBridgeWithAnIndexedReference_ResolvesToThatInstance()
        {
            var vnets = string.Join(",", new[] { 0, 1 }.Select(i => $$$$"""
                {"address":"proxmox_sdn_vnet.lan[{{{{i}}}}]","mode":"managed","type":"proxmox_sdn_vnet","name":"lan",
                 "change":{"actions":["create"],"after":{"zone":"z"}}}
                """));
            var configuration = """
                {"resources":[{"address":"proxmox_virtual_environment_vm.a","expressions":
                  {"network_device":[{"bridge":{"references":["proxmox_sdn_vnet.lan[1].id","proxmox_sdn_vnet.lan[1]","proxmox_sdn_vnet.lan"]}}]}}]}
                """;

            var topology = PlanTopologyParser.Parse(Plan(
                Vm("proxmox_virtual_environment_vm.a", "\"create\"", "{}") + "," + vnets,
                configuration));

            Assert.Equal(["proxmox_sdn_vnet.lan[1]"], NetworksOf(topology, "proxmox_virtual_environment_vm.a"));
        }

        [Fact]
        public void PlanWithNoKnownTypes_IsEmpty()
        {
            var topology = PlanTopologyParser.Parse(Plan("""
                {"address":"null_resource.x","mode":"managed","type":"null_resource","name":"x",
                 "change":{"actions":["create"],"after":{}}}
                """));

            Assert.Empty(topology.Nodes);
            Assert.Empty(topology.Edges);
        }

        #endregion
    }
}
