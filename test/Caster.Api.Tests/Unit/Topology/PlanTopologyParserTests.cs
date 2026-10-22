// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.Topology;
using Caster.Api.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
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

        private static string Resource(string address, string after, string mode = "managed") =>
            $$$$"""
            {"address":"{{{{address}}}}","mode":"{{{{mode}}}}","type":"{{{{address.Split('.')[0]}}}}","name":"{{{{address.Split('.')[1]}}}}",
             "change":{"actions":["create"],"after":{{{{after}}}}}}
            """;

        [Fact]
        public void ClonedVm_ReadsItsMapOfNicsInSlotOrder()
        {
            var topology = PlanTopologyParser.Parse(Plan(Resource("proxmox_cloned_vm.fw", """
                {"name":"fw","node_name":"pve","cpu":{"cores":2},"memory":{"size":2048},
                 "network":{"net10":{"bridge":"c"},"net2":{"bridge":"b","tag":20},"net0":{"bridge":"a"}}}
                """)));

            Assert.Equal(["external:a", "external:b.20", "external:c"], NetworksOf(topology, "proxmox_cloned_vm.fw"));
            var fw = topology.Nodes.Single(x => x.Kind == TopologyNodeKind.Machine);
            Assert.Equal("2", fw.Properties["cpus"]);
            Assert.Equal("2048", fw.Properties["memory_mb"]);
        }

        [Fact]
        public void Containers_AreMachinesLabelledByHostname()
        {
            var topology = PlanTopologyParser.Parse(Plan(
                Resource("proxmox_virtual_environment_container.ct", """
                    {"initialization":[{"hostname":"web01"}],"network_interface":[{"name":"eth0","bridge":"lan"}]}
                    """) + "," +
                Resource("proxmox_lxc.old", """{"hostname":"web02","network":[{"name":"eth0","bridge":"lan","tag":5}]}""") + "," +
                Resource("proxmox_virtual_environment_sdn_vnet.lan", """{"id":"lan","zone":"z"}""")));

            Assert.Equal("web01", topology.Nodes.Single(x => x.Id == "proxmox_virtual_environment_container.ct").Label);
            Assert.Equal("web02", topology.Nodes.Single(x => x.Id == "proxmox_lxc.old").Label);
            Assert.Equal(["proxmox_virtual_environment_sdn_vnet.lan"], NetworksOf(topology, "proxmox_virtual_environment_container.ct"));
            Assert.Equal(["external:lan.5"], NetworksOf(topology, "proxmox_lxc.old"));
        }

        [Fact]
        public void ShortProxmoxNames_AreDrawnLikeTheLongOnes()
        {
            var topology = PlanTopologyParser.Parse(Plan(
                Resource("proxmox_vm.new", """{"name":"new","cpu":{"cores":4}}""") + "," +
                Resource("proxmox_network_linux_bridge.br", """{"name":"vmbr9"}""") + "," +
                Vm("proxmox_virtual_environment_vm.a", "\"create\"", """{"bridge":"vmbr9"}""")));

            // proxmox_vm has no NICs to read yet: drawn, unconnected.
            Assert.Equal("4", topology.Nodes.Single(x => x.Id == "proxmox_vm.new").Properties["cpus"]);
            Assert.Empty(NetworksOf(topology, "proxmox_vm.new"));
            Assert.Equal(["proxmox_network_linux_bridge.br"], NetworksOf(topology, "proxmox_virtual_environment_vm.a"));
        }

        [Fact]
        public void AwsInstance_IsItsOwnNicOnItsSubnet()
        {
            var topology = PlanTopologyParser.Parse(Plan(
                Resource("aws_instance.web", """{"subnet_id":"subnet-1","instance_type":"t3.micro","tags":{"Name":"web"}}""") + "," +
                Resource("aws_subnet.dmz", """{"id":"subnet-1","cidr_block":"10.1.0.0/24"}""")));

            var web = topology.Nodes.Single(x => x.Id == "aws_instance.web");
            Assert.Equal("web", web.Label);
            Assert.Equal("t3.micro", web.Properties["instance_type"]);
            Assert.Equal("10.1.0.0/24", topology.Nodes.Single(x => x.Id == "aws_subnet.dmz").Label);
            Assert.Equal(["aws_subnet.dmz"], NetworksOf(topology, "aws_instance.web"));
        }

        [Fact]
        public void AwsSubnetCreatedInThePlan_IsFoundByReference()
        {
            var configuration = """
                {"resources":[{"address":"aws_instance.web","expressions":
                  {"subnet_id":{"references":["aws_subnet.dmz.id","aws_subnet.dmz"]}}}]}
                """;

            var topology = PlanTopologyParser.Parse(Plan(
                Resource("aws_instance.web", """{"instance_type":"t3.micro"}""") + "," +
                Resource("aws_subnet.dmz", """{"cidr_block":"10.1.0.0/24"}"""),
                configuration));

            Assert.Equal(["aws_subnet.dmz"], NetworksOf(topology, "aws_instance.web"));
        }

        [Fact]
        public void AzureVm_IsWiredThroughItsNicsToTheirSubnets()
        {
            var topology = PlanTopologyParser.Parse(Plan(
                Resource("azurerm_linux_virtual_machine.web", """
                    {"name":"web","size":"Standard_B1s","location":"eastus","network_interface_ids":["nic-a","nic-b"]}
                    """) + "," +
                Resource("azurerm_network_interface.a", """{"id":"nic-a","ip_configuration":[{"name":"ip","subnet_id":"subnet-dmz"}]}""") + "," +
                Resource("azurerm_network_interface.b", """{"id":"nic-b","ip_configuration":[{"name":"ip","subnet_id":"subnet-other"}]}""") + "," +
                Resource("azurerm_subnet.dmz", """{"id":"subnet-dmz","name":"dmz"}""")));

            var web = topology.Nodes.Single(x => x.Id == "azurerm_linux_virtual_machine.web");
            Assert.Equal("Standard_B1s", web.Properties["size"]);
            Assert.Equal(["azurerm_subnet.dmz", "external:subnet-other"], NetworksOf(topology, "azurerm_linux_virtual_machine.web"));
            // The NICs are the wires, not nodes.
            Assert.DoesNotContain(topology.Nodes, x => x.Type == "azurerm_network_interface");
        }

        [Fact]
        public void AzureNicsAndSubnetsCreatedInThePlan_AreFoundByReference()
        {
            var configuration = """
                {"resources":[
                  {"address":"azurerm_windows_virtual_machine.dc","expressions":
                    {"network_interface_ids":{"references":["azurerm_network_interface.a.id","azurerm_network_interface.a",
                                                            "azurerm_network_interface.b.id","azurerm_network_interface.b"]}}},
                  {"address":"azurerm_network_interface.a","expressions":
                    {"ip_configuration":[{"subnet_id":{"references":["azurerm_subnet.servers.id","azurerm_subnet.servers"]}}]}},
                  {"address":"azurerm_network_interface.b","expressions":
                    {"ip_configuration":[{"subnet_id":{"references":["azurerm_subnet.mgmt.id","azurerm_subnet.mgmt"]}}]}}]}
                """;

            var topology = PlanTopologyParser.Parse(Plan(
                Resource("azurerm_windows_virtual_machine.dc", """{"name":"dc","size":"Standard_B2s"}""") + "," +
                Resource("azurerm_network_interface.a", """{"ip_configuration":[{"name":"ip"}]}""") + "," +
                Resource("azurerm_network_interface.b", """{"ip_configuration":[{"name":"ip"}]}""") + "," +
                Resource("azurerm_subnet.servers", """{"name":"servers"}""") + "," +
                Resource("azurerm_subnet.mgmt", """{"name":"mgmt"}"""),
                configuration));

            var edges = topology.Edges.Where(x => x.From == "azurerm_windows_virtual_machine.dc").OrderBy(x => x.Nic).ToList();
            Assert.Equal(["azurerm_subnet.servers", "azurerm_subnet.mgmt"], edges.Select(x => x.To));
            Assert.Equal([0, 1], edges.Select(x => x.Nic));
        }

        [Fact]
        public void AzureNicIndexedByEachKey_IsTheMachinesOwn()
        {
            var nics = string.Join(",", new[] { "web", "db" }.Select(key => $$$$"""
                {"address":"azurerm_network_interface.nic[\"{{{{key}}}}\"]","mode":"managed","type":"azurerm_network_interface","name":"nic",
                 "change":{"actions":["create"],"after":{"ip_configuration":[{"name":"ip","subnet_id":"subnet-{{{{key}}}}"}]}}}
                """));
            var configuration = """
                {"resources":[{"address":"azurerm_linux_virtual_machine.vm","expressions":
                  {"network_interface_ids":{"references":["azurerm_network_interface.nic","each.key"]}}}]}
                """;

            var topology = PlanTopologyParser.Parse(Plan(
                """
                {"address":"azurerm_linux_virtual_machine.vm[\"db\"]","mode":"managed","type":"azurerm_linux_virtual_machine","name":"vm",
                 "change":{"actions":["create"],"after":{"name":"db"}}}
                """ + "," + nics,
                configuration));

            Assert.Equal(["external:subnet-db"], NetworksOf(topology, "azurerm_linux_virtual_machine.vm[\"db\"]"));
        }

        [Fact]
        public void ConfiguredTypes_AreDrawn()
        {
            var options = new TopologyOptions
            {
                Machines =
                {
                    ["google_compute_instance"] = new TopologyMachineType
                    {
                        Nics = "network_interface",
                        Network = "subnetwork",
                        Properties = { ["machine_type"] = "machine_type" },
                    },
                },
                Networks = { ["google_compute_subnetwork"] = new TopologyNetworkType { Keys = ["self_link", "name"] } },
            };

            var topology = PlanTopologyParser.Parse(Plan(
                Resource("google_compute_instance.web", """
                    {"name":"web","machine_type":"e2-micro","network_interface":[{"subnetwork":"dmz"}]}
                    """) + "," +
                Resource("google_compute_subnetwork.dmz", """{"name":"dmz"}""")), options);

            Assert.Equal("e2-micro", topology.Nodes.Single(x => x.Id == "google_compute_instance.web").Properties["machine_type"]);
            Assert.Equal(["google_compute_subnetwork.dmz"], NetworksOf(topology, "google_compute_instance.web"));
        }

        [Fact]
        public void ConfiguredTypes_BindFromTheTerraformSection()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Terraform:Topology:Machines:example_vm:Interfaces"] = "nic_ids",
                    ["Terraform:Topology:Machines:example_vm:Label:0"] = "hostname",
                    ["Terraform:Topology:Interfaces:example_nic:Keys:0"] = "id",
                    ["Terraform:Topology:Interfaces:example_nic:Network"] = "network",
                    ["Terraform:Topology:Networks:example_network:Keys:0"] = "id",
                })
                .Build();
            var options = configuration.GetSection("Terraform").Get<TerraformOptions>();

            var topology = PlanTopologyParser.Parse(Plan(
                Resource("example_vm.web", """{"hostname":"web","nic_ids":["n1"]}""") + "," +
                Resource("example_nic.n", """{"id":"n1","network":"net-1"}""") + "," +
                Resource("example_network.lan", """{"id":"net-1"}""")), options.Topology);

            Assert.Equal(["hostname"], options.Topology.Machines["example_vm"].Label);
            Assert.Equal(["example_network.lan"], NetworksOf(topology, "example_vm.web"));
        }

        [Fact]
        public void ConfiguredType_ReplacesTheBuiltInOfTheSameName()
        {
            var options = new TopologyOptions
            {
                Machines = { ["proxmox_virtual_environment_vm"] = new TopologyMachineType { Nics = "network_device", Network = "bridge", Label = ["description"] } },
            };

            var topology = PlanTopologyParser.Parse(Plan(Resource("proxmox_virtual_environment_vm.a", """
                {"name":"a","description":"Domain controller","cpu":[{"cores":2}],"network_device":[{"bridge":"vmbr0","vlan_id":10}]}
                """)), options);

            var vm = topology.Nodes.Single(x => x.Kind == TopologyNodeKind.Machine);
            Assert.Equal("Domain controller", vm.Label);
            // No Vlan or Properties configured: the replacement has neither.
            Assert.Empty(vm.Properties);
            Assert.Equal(["external:vmbr0"], NetworksOf(topology, "proxmox_virtual_environment_vm.a"));
        }

        private const string Admin = "11111111-1111-1111-1111-111111111111";
        private const string Red = "22222222-2222-2222-2222-222222222222";

        [Fact]
        public void PlayerRecord_GivesItsMachineConsoleTeams_ByVmId()
        {
            var topology = PlanTopologyParser.Parse(Plan(
                Resource("proxmox_virtual_environment_vm.dc", """{"name":"dc","vm_id":200,"node_name":"pve"}""") + "," +
                Resource("proxmox_virtual_environment_vm.ws", """{"name":"ws","vm_id":201,"node_name":"pve"}""") + "," +
                Resource("crucible_player_virtual_machine.dc", $$"""
                    {"name":"domain controller","team_ids":["{{Admin}}","{{Red}}"],"proxmox_vm_info":[{"id":"200","node":"pve"}]}
                    """)));

            var dc = topology.Nodes.Single(x => x.Id == "proxmox_virtual_environment_vm.dc").Console;
            Assert.Equal([Admin, Red], dc.TeamIds);
            Assert.Equal(TopologyConsoleMatch.Id, dc.Match);
            // Registered nowhere: no team can open it.
            var ws = topology.Nodes.Single(x => x.Id == "proxmox_virtual_environment_vm.ws").Console;
            Assert.Empty(ws.TeamIds);
            Assert.Null(ws.Match);
        }

        [Fact]
        public void PlayerRecord_WhoseVmIdIsKnownAfterApply_IsMatchedByReference()
        {
            var configuration = """
                {"resources":[{"address":"crucible_player_virtual_machine.dc","expressions":
                  {"proxmox_vm_info":[{"id":{"references":["proxmox_virtual_environment_vm.dc.vm_id","proxmox_virtual_environment_vm.dc"]}}]}}]}
                """;

            var topology = PlanTopologyParser.Parse(Plan(
                Resource("proxmox_virtual_environment_vm.dc", """{"name":"dc","node_name":"pve"}""") + "," +
                Resource("crucible_player_virtual_machine.dc", $$"""{"name":"x","team_ids":["{{Admin}}"],"proxmox_vm_info":[{"node":"pve"}]}"""),
                configuration));

            var dc = topology.Nodes.Single(x => x.Kind == TopologyNodeKind.Machine).Console;
            Assert.Equal([Admin], dc.TeamIds);
            Assert.Equal(TopologyConsoleMatch.Reference, dc.Match);
        }

        [Fact]
        public void PlayerRecord_WithOnlyAName_IsAGuessByName()
        {
            // for_each over a local map: the record refers to each.value, not the VM.
            var topology = PlanTopologyParser.Parse(Plan(
                Resource("proxmox_virtual_environment_vm.core_dc", """{"name":"core-dc","node_name":"pve"}""") + "," +
                Resource("crucible_player_virtual_machine.range[\\\"core-dc\\\"]", $$"""{"name":"core-dc","team_ids":["{{Admin}}"]}""")));

            var dc = topology.Nodes.Single(x => x.Kind == TopologyNodeKind.Machine).Console;
            Assert.Equal([Admin], dc.TeamIds);
            Assert.Equal(TopologyConsoleMatch.Name, dc.Match);
        }

        [Fact]
        public void PlayerRecord_WhoseTeamsAreKnownAfterApply_SaysSo()
        {
            var topology = PlanTopologyParser.Parse(Plan(
                Resource("proxmox_virtual_environment_vm.dc", """{"name":"dc","vm_id":200,"node_name":"pve"}""") + "," +
                Resource("crucible_player_virtual_machine.dc", """{"name":"dc","proxmox_vm_info":[{"id":"200","node":"pve"}]}""")));

            var dc = topology.Nodes.Single(x => x.Kind == TopologyNodeKind.Machine).Console;
            Assert.True(dc.TeamsUnknown);
            Assert.Empty(dc.TeamIds);
        }

        [Fact]
        public void VsphereGuestInfoTeams_AreTheMachinesOwn()
        {
            var topology = PlanTopologyParser.Parse(Plan(
                Resource("vsphere_virtual_machine.dc", $$$"""{"name":"dc","extra_config":{"guestinfo.teamId":"{{{Admin}}},{{{Red}}}"}}""")));

            var dc = topology.Nodes.Single(x => x.Kind == TopologyNodeKind.Machine).Console;
            Assert.Equal([Admin, Red], dc.TeamIds);
            Assert.Equal(TopologyConsoleMatch.Self, dc.Match);
        }

        [Fact]
        public void PlayerRecord_GivesItsMachineItsOwnConsole()
        {
            const string vmId = "33333333-3333-3333-3333-333333333333";
            var topology = PlanTopologyParser.Parse(Plan(
                Resource("proxmox_virtual_environment_vm.dc", """{"name":"dc","vm_id":200,"node_name":"pve"}""") + "," +
                Resource("crucible_player_virtual_machine.dc", $$"""
                    {"id":"{{vmId}}","url":"http://console/vm/{{vmId}}/console","name":"dc","team_ids":["{{Admin}}"],"proxmox_vm_info":[{"id":"200","node":"pve"}]}
                    """)));

            var dc = topology.Nodes.Single(x => x.Kind == TopologyNodeKind.Machine).Console;
            Assert.Equal(vmId, dc.VmId);
            Assert.Equal("dc", dc.Name);
            Assert.Equal($"http://console/vm/{vmId}/console", dc.Url);
        }

        [Fact]
        public void TeamPassedInAsAVariable_IsNamedAfterIt()
        {
            // How Alloy passes in its view's teams.
            var plan = Plan(
                Resource("proxmox_virtual_environment_vm.dc", """{"name":"dc","vm_id":200,"node_name":"pve"}""") + "," +
                Resource("crucible_player_virtual_machine.dc", $$"""
                    {"name":"dc","team_ids":["{{Admin}}","{{Red}}"],"proxmox_vm_info":[{"id":"200","node":"pve"}]}
                    """));
            plan = $$$"""{"variables":{"admin_user":{"value":"{{{Admin}}}"},"view_id":{"value":"44444444-4444-4444-4444-444444444444"},"username":{"value":"admin"}},""" + plan[1..];

            var teams = PlanTopologyParser.Parse(plan).Nodes.Single(x => x.Kind == TopologyNodeKind.Machine).Console.Teams;
            Assert.Equal([Admin, Red], teams.Select(x => x.Id));
            Assert.Equal("admin_user", teams[0].Name);
            Assert.Equal("admin_user", teams[0].Variable);
            Assert.Null(teams[1].Name);
        }

        [Fact]
        public void TeamTheWorkspaceCreates_IsNamedByIt()
        {
            var topology = PlanTopologyParser.Parse(Plan(
                Resource("crucible_player_view.v", $$"""{"name":"view","team":[{"team_id":"{{Admin}}","name":"Admin"}]}""") + "," +
                Resource("crucible_player_team.red", $$"""{"id":"{{Red}}","name":"Red Cell"}""") + "," +
                Resource("proxmox_virtual_environment_vm.dc", """{"name":"dc","vm_id":200,"node_name":"pve"}""") + "," +
                Resource("crucible_player_virtual_machine.dc", $$"""
                    {"name":"dc","team_ids":["{{Admin}}","{{Red}}"],"proxmox_vm_info":[{"id":"200","node":"pve"}]}
                    """)));

            var teams = topology.Nodes.Single(x => x.Kind == TopologyNodeKind.Machine).Console.Teams;
            Assert.Equal(["Admin", "Red Cell"], teams.Select(x => x.Name));
            Assert.All(teams, x => Assert.Null(x.Variable));
        }

        [Fact]
        public void TeamInAWorkspaceTfvarsFile_IsNamedAfterItsVariable()
        {
            // A state, or a plan stored before teams were named, has only ids.
            var topology = PlanTopologyParser.Parse(Plan(
                Resource("proxmox_virtual_environment_vm.dc", """{"name":"dc","vm_id":200,"node_name":"pve"}""") + "," +
                Resource("crucible_player_virtual_machine.dc", $$"""
                    {"name":"dc","team_ids":["{{Admin}}","{{Red}}"],"proxmox_vm_info":[{"id":"200","node":"pve"}]}
                    """)));

            TopologyTeamNames.Fill(topology, TopologyTeamNames.FromTfvars([$$"""
                exercise_id = "not an id"
                admin = "{{Admin}}"
                # red = "{{Red}}"
                """]));

            var teams = topology.Nodes.Single(x => x.Kind == TopologyNodeKind.Machine).Console.Teams;
            Assert.Equal("admin", teams[0].Name);
            Assert.Null(teams[1].Name);
        }

        [Fact]
        public void WorkspaceThatRegistersNothingInPlayer_SaysNothingAboutConsoles()
        {
            var topology = PlanTopologyParser.Parse(Plan(
                Resource("proxmox_virtual_environment_vm.dc", """{"name":"dc","vm_id":200,"node_name":"pve"}""")));

            Assert.Null(topology.Nodes.Single().Console);
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
