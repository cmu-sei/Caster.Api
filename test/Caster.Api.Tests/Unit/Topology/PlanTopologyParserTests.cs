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
        public void PlayerRecordThatChanges_SaysSoOnItsMachine()
        {
            var topology = PlanTopologyParser.Parse(Plan("""
                {"address":"proxmox_virtual_environment_vm.dc","mode":"managed","type":"proxmox_virtual_environment_vm","name":"dc",
                 "change":{"actions":["no-op"],"after":{"name":"dc-1234","vm_id":200,"node_name":"pve"}}},
                {"address":"crucible_player_virtual_machine.dc","mode":"managed","type":"crucible_player_virtual_machine","name":"dc",
                 "change":{"actions":["update"],"after":{"name":"dc","team_ids":[],"proxmox_vm_info":[{"id":"200","node":"pve"}]}}}
                """));

            var dc = topology.Nodes.Single(x => x.Kind == TopologyNodeKind.Machine);
            // The machine itself is left alone; its Player record changes.
            Assert.Equal("no-op", dc.Action);
            Assert.Equal("update", dc.Console.Action);
            Assert.Equal("crucible_player_virtual_machine.dc", dc.Console.Address);
        }

        [Fact]
        public void WorkspaceThatRegistersNothingInPlayer_SaysNothingAboutConsoles()
        {
            var topology = PlanTopologyParser.Parse(Plan(
                Resource("proxmox_virtual_environment_vm.dc", """{"name":"dc","vm_id":200,"node_name":"pve"}""")));

            Assert.Null(topology.Nodes.Single().Console);
        }

        [Fact]
        public void SensitiveValue_IsNeitherLabelNorProperty()
        {
            var plan = """
                {"format_version":"1.2","resource_changes":[
                  {"address":"aws_subnet.b","mode":"managed","type":"aws_subnet","name":"b",
                   "change":{"actions":["create"],"after":{"id":"subnet-b","cidr_block":"10.7.0.0/24","tags":{"Name":"secret-name"}},
                             "after_sensitive":{"cidr_block":true,"tags":{"Name":true}}}},
                  {"address":"aws_instance.web","mode":"managed","type":"aws_instance","name":"web",
                   "change":{"actions":["create"],"after":{"subnet_id":"subnet-b","instance_type":"t3.secret","tags":{"Name":"web"}},
                             "after_sensitive":{"instance_type":true}}}]}
                """;

            var topology = PlanTopologyParser.Parse(plan);

            // Not tags.Name, not cidr_block: the resource's own name.
            Assert.Equal("b", topology.Nodes.Single(x => x.Id == "aws_subnet.b").Label);
            Assert.Equal("web", topology.Nodes.Single(x => x.Id == "aws_instance.web").Label);
            Assert.False(topology.Nodes.Single(x => x.Id == "aws_instance.web").Properties.ContainsKey("instance_type"));
            var json = System.Text.Json.JsonSerializer.Serialize(topology);
            Assert.DoesNotContain("10.7.0", json);
            Assert.DoesNotContain("secret", json);
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

        #region Addresses and CIDRs

        private static string Change(
            string address, string after, string actions = "\"create\"", string unknown = "{}", string sensitive = "{}") =>
            $$$$"""
            {"address":"{{{{address}}}}","mode":"managed","type":"{{{{address.Split('.')[0]}}}}","name":"{{{{address.Split('.')[1]}}}}",
             "change":{"actions":[{{{{actions}}}}],"after":{{{{after}}}},"after_unknown":{{{{unknown}}}},"after_sensitive":{{{{sensitive}}}}}}
            """;

        private static string State(params string[] resources) =>
            $$"""{"version":4,"terraform_version":"1.15.0","resources":[{{string.Join(",", resources)}}]}""";

        private static string StateResource(string address, string attributes, string sensitive = "[]") =>
            $$$$"""
            {"mode":"managed","type":"{{{{address.Split('.')[0]}}}}","name":"{{{{address.Split('.')[1]}}}}",
             "instances":[{"schema_version":0,"attributes":{{{{attributes}}}},"sensitive_attributes":{{{{sensitive}}}}}]}
            """;

        private static Dictionary<string, string> PropertiesOf(Caster.Api.Domain.Models.Topology topology, string id) =>
            topology.Nodes.Single(x => x.Id == id).Properties;

        private const string ThreeVnets = """
            {"address":"proxmox_sdn_vnet.a","mode":"managed","type":"proxmox_sdn_vnet","name":"a","change":{"actions":["create"],"after":{"id":"a","zone":"z"}}},
            {"address":"proxmox_sdn_vnet.b","mode":"managed","type":"proxmox_sdn_vnet","name":"b","change":{"actions":["create"],"after":{"id":"b","zone":"z"}}},
            {"address":"proxmox_sdn_vnet.c","mode":"managed","type":"proxmox_sdn_vnet","name":"c","change":{"actions":["create"],"after":{"id":"c","zone":"z"}}}
            """;

        [Fact]
        public void ProxmoxCloudInit_GivesAddressesInNicOrderIpv4First()
        {
            var topology = PlanTopologyParser.Parse(Plan(Change("proxmox_virtual_environment_vm.dc", """
                {"name":"dc","network_device":[{"bridge":"a"},{"bridge":"b"},{"bridge":"c"}],
                 "initialization":[{"ip_config":[
                   {"ipv4":[{"address":"10.0.1.5/24","gateway":"10.0.1.1"}],"ipv6":[{"address":"fd00:1::5/64"}]},
                   {"ipv4":[{"address":"dhcp"}],"ipv6":[{"address":"auto"}]},
                   {"ipv4":[{"address":"10.0.3.5/24","gateway":"10.0.3.1"}]}]}]}
                """) + "," + ThreeVnets));

            var dc = PropertiesOf(topology, "proxmox_virtual_environment_vm.dc");
            Assert.Equal("10.0.1.5,10.0.3.5,fd00:1::5", dc["ip_addresses"]);
            // The first NIC's gateway.
            Assert.Equal("10.0.1.1", dc["gateway"]);
            Assert.Equal(
                "proxmox_sdn_vnet.a=10.0.1.5;proxmox_sdn_vnet.c=10.0.3.5;proxmox_sdn_vnet.a=fd00:1::5",
                dc["ip_addresses_by_network"]);
        }

        [Fact]
        public void ProxmoxDhcpEverywhere_HasNoAddresses()
        {
            var topology = PlanTopologyParser.Parse(Plan(Change("proxmox_virtual_environment_vm.ws", """
                {"name":"ws","network_device":[{"bridge":"vmbr0"}],
                 "initialization":[{"ip_config":[{"ipv4":[{"address":"dhcp"}],"ipv6":[{"address":"dhcp"}]}]}]}
                """)));

            var ws = PropertiesOf(topology, "proxmox_virtual_environment_vm.ws");
            Assert.False(ws.ContainsKey("ip_addresses"));
            Assert.False(ws.ContainsKey("gateway"));
            Assert.False(ws.ContainsKey("ip_addresses_by_network"));
        }

        [Fact]
        public void TheSameAddressOnTwoNics_IsListedOnce()
        {
            var topology = PlanTopologyParser.Parse(Plan(Change("proxmox_virtual_environment_vm.fw", """
                {"name":"fw","network_device":[{"bridge":"a"},{"bridge":"b"}],
                 "initialization":[{"ip_config":[{"ipv4":[{"address":"10.0.0.1/24"}]},{"ipv4":[{"address":"10.0.0.1/24"}]}]}]}
                """) + "," + ThreeVnets));

            var fw = PropertiesOf(topology, "proxmox_virtual_environment_vm.fw");
            Assert.Equal("10.0.0.1", fw["ip_addresses"]);
            Assert.Equal("proxmox_sdn_vnet.a=10.0.0.1;proxmox_sdn_vnet.b=10.0.0.1", fw["ip_addresses_by_network"]);
        }

        [Fact]
        public void ProxmoxContainerAndClonedVm_ReadTheirIpConfig()
        {
            var topology = PlanTopologyParser.Parse(Plan(
                Change("proxmox_virtual_environment_container.ct", """
                    {"initialization":[{"hostname":"web","ip_config":[{"ipv4":[{"address":"10.0.2.20/24","gateway":"10.0.2.1"}]}]}],
                     "network_interface":[{"name":"eth0","bridge":"b"}]}
                    """) + "," +
                Change("proxmox_cloned_vm.fw", """
                    {"name":"fw","network":{"net0":{"bridge":"a"}},
                     "initialization":{"ip_config":[{"ipv4":{"address":"10.0.1.254/24"}}]}}
                    """) + "," + ThreeVnets));

            Assert.Equal("10.0.2.20", PropertiesOf(topology, "proxmox_virtual_environment_container.ct")["ip_addresses"]);
            Assert.Equal("10.0.2.1", PropertiesOf(topology, "proxmox_virtual_environment_container.ct")["gateway"]);
            Assert.Equal("proxmox_sdn_vnet.a=10.0.1.254", PropertiesOf(topology, "proxmox_cloned_vm.fw")["ip_addresses_by_network"]);
        }

        [Fact]
        public void TelmateIpConfigStrings_AreParsed()
        {
            var topology = PlanTopologyParser.Parse(Plan(
                Change("proxmox_vm_qemu.dc", """
                    {"name":"dc","network":[{"bridge":"a"},{"bridge":"b"}],
                     "ipconfig0":"ip=10.0.1.10/24,gw=10.0.1.1,ip6=2001:db8:1::10/64","ipconfig1":"ip=dhcp,ip6=auto"}
                    """) + "," +
                Change("proxmox_lxc.web", """
                    {"hostname":"web","network":[{"name":"eth0","bridge":"b","ip":"10.0.2.10/24","gw":"10.0.2.1","ip6":"manual"}]}
                    """) + "," + ThreeVnets));

            var dc = PropertiesOf(topology, "proxmox_vm_qemu.dc");
            Assert.Equal("10.0.1.10,2001:db8:1::10", dc["ip_addresses"]);
            Assert.Equal("10.0.1.1", dc["gateway"]);

            var web = PropertiesOf(topology, "proxmox_lxc.web");
            Assert.Equal("10.0.2.10", web["ip_addresses"]);
            Assert.Equal("10.0.2.1", web["gateway"]);
            Assert.Equal("proxmox_sdn_vnet.b=10.0.2.10", web["ip_addresses_by_network"]);
        }

        [Fact]
        public void AddressKnownOnlyAfterApply_IsLeftOut()
        {
            // Terraform leaves an unknown value out of "after"; the mask is
            // honoured even if a value is there.
            var topology = PlanTopologyParser.Parse(Plan(Change(
                "proxmox_virtual_environment_vm.dc",
                """
                {"name":"dc","network_device":[{"bridge":"a"},{"bridge":"b"}],
                 "initialization":[{"ip_config":[{"ipv4":[{"address":"10.0.1.5/24"}]},{"ipv4":[{"address":"10.0.2.5/24"}]}]}]}
                """,
                unknown: """{"initialization":[{"ip_config":[{"ipv4":[{"address":true}]},{}]}]}""") + "," + ThreeVnets));

            Assert.Equal("10.0.2.5", PropertiesOf(topology, "proxmox_virtual_environment_vm.dc")["ip_addresses"]);
        }

        [Fact]
        public void SensitiveAddress_IsLeftOutAndAppearsNowhere()
        {
            var topology = PlanTopologyParser.Parse(Plan(Change(
                "proxmox_virtual_environment_vm.dc",
                """
                {"name":"dc","network_device":[{"bridge":"a"}],
                 "initialization":[{"ip_config":[{"ipv4":[{"address":"10.9.9.9/24","gateway":"10.9.9.1"}]}]}]}
                """,
                sensitive: """{"initialization":true}""") + "," + ThreeVnets));

            var dc = PropertiesOf(topology, "proxmox_virtual_environment_vm.dc");
            Assert.False(dc.ContainsKey("ip_addresses"));
            Assert.False(dc.ContainsKey("gateway"));
            Assert.DoesNotContain("10.9.9", System.Text.Json.JsonSerializer.Serialize(topology));
        }

        [Fact]
        public void SensitiveAddressInThePriorState_IsLeftOut()
        {
            var plan = """
                {"format_version":"1.2","resource_changes":[],"prior_state":{"values":{"root_module":{"resources":[
                  {"address":"aws_instance.web","mode":"managed","type":"aws_instance","name":"web",
                   "values":{"private_ip":"10.1.0.5","public_ip":"203.0.113.9"},"sensitive_values":{"public_ip":true}}]}}}}
                """;

            var web = PropertiesOf(PlanTopologyParser.Parse(plan), "aws_instance.web");
            Assert.Equal("10.1.0.5", web["ip_addresses"]);
        }

        [Fact]
        public void SensitiveAddressInTheState_IsLeftOut()
        {
            var topology = PlanTopologyParser.ParseState(State(StateResource(
                "proxmox_virtual_environment_vm.dc",
                """
                {"name":"dc","network_device":[{"bridge":"vmbr0"},{"bridge":"vmbr1"}],
                 "initialization":[{"ip_config":[{"ipv4":[{"address":"10.9.9.9/24"}]},{"ipv4":[{"address":"10.0.2.5/24"}]}]}]}
                """,
                """
                [[{"type":"get_attr","value":"initialization"},{"type":"index","value":{"value":0,"type":"number"}},
                  {"type":"get_attr","value":"ip_config"},{"type":"index","value":{"value":0,"type":"number"}}]]
                """)));

            Assert.Equal("10.0.2.5", PropertiesOf(topology, "proxmox_virtual_environment_vm.dc")["ip_addresses"]);
            Assert.DoesNotContain("10.9.9", System.Text.Json.JsonSerializer.Serialize(topology));
        }

        private const string VsphereClone = """
            "network_interface":[{"network_id":"network-1"},{"network_id":"network-2"}],
            "clone":[{"customize":[{"ipv4_gateway":"10.0.0.1","network_interface":[{"ipv4_address":"10.0.0.10","ipv4_netmask":24},{"ipv4_address":"10.0.1.10","ipv4_netmask":24}]}]}]
            """;

        [Fact]
        public void VspherePlan_UsesTheCustomizeAddresses()
        {
            var topology = PlanTopologyParser.Parse(Plan(Change(
                "vsphere_virtual_machine.dc",
                $$"""{"name":"dc",{{VsphereClone}}}""",
                unknown: """{"default_ip_address":true,"guest_ip_addresses":true}""")));

            var dc = PropertiesOf(topology, "vsphere_virtual_machine.dc");
            Assert.Equal("10.0.0.10,10.0.1.10", dc["ip_addresses"]);
            Assert.Equal("10.0.0.1", dc["gateway"]);
            Assert.Equal("external:network-1=10.0.0.10;external:network-2=10.0.1.10", dc["ip_addresses_by_network"]);
        }

        [Fact]
        public void VsphereState_PrefersTheGuestsOwnAddresses()
        {
            // Customize has no address: the clone uses DHCP.
            var dhcp = PlanTopologyParser.ParseState(State(StateResource("vsphere_virtual_machine.ws", """
                {"name":"ws","network_interface":[{"network_id":"network-1"}],
                 "clone":[{"customize":[{"network_interface":[{"ipv4_address":""}]}]}],
                 "default_ip_address":"10.0.0.57","guest_ip_addresses":["10.0.0.57","fe80::250:56ff:fe01:2","2001:db8::57"]}
                """)));

            Assert.Equal("10.0.0.57,2001:db8::57", PropertiesOf(dhcp, "vsphere_virtual_machine.ws")["ip_addresses"]);
            Assert.False(PropertiesOf(dhcp, "vsphere_virtual_machine.ws").ContainsKey("ip_addresses_by_network"));

            // A static clone whose guest has since been given another address.
            var changed = PlanTopologyParser.ParseState(State(StateResource("vsphere_virtual_machine.dc", $$"""
                {"name":"dc",{{VsphereClone}},"default_ip_address":"10.0.0.10","guest_ip_addresses":["10.0.0.10","10.0.0.99"]}
                """)));

            var dc = PropertiesOf(changed, "vsphere_virtual_machine.dc");
            Assert.Equal("10.0.0.10,10.0.0.99", dc["ip_addresses"]);
            // Only the address the guest still has is placed on its network.
            Assert.Equal("external:network-1=10.0.0.10", dc["ip_addresses_by_network"]);
        }

        [Fact]
        public void VspherePlanThatChangesTheMachine_PrefersItsCustomizeAddresses()
        {
            var topology = PlanTopologyParser.Parse(Plan(Change(
                "vsphere_virtual_machine.dc",
                $$"""{"name":"dc",{{VsphereClone}},"default_ip_address":"10.0.0.99","guest_ip_addresses":["10.0.0.99"]}""",
                actions: "\"update\"")));

            Assert.Equal("10.0.0.10,10.0.1.10", PropertiesOf(topology, "vsphere_virtual_machine.dc")["ip_addresses"]);
        }

        [Fact]
        public void AzureNicAddresses_AreOnTheirMachine()
        {
            var topology = PlanTopologyParser.Parse(Plan(
                Change("azurerm_linux_virtual_machine.web", """{"name":"web","network_interface_ids":["nic-a","nic-b"]}""", "\"no-op\"") + "," +
                Change("azurerm_network_interface.a", """
                    {"id":"nic-a","ip_configuration":[{"name":"ip","subnet_id":"subnet-dmz","private_ip_address":"10.2.1.4","public_ip_address_id":"pip-1"}]}
                    """, "\"no-op\"") + "," +
                Change("azurerm_network_interface.b", """
                    {"id":"nic-b","ip_configuration":[{"name":"ip","subnet_id":"subnet-app","private_ip_address":"10.2.2.4"},
                                                      {"name":"ip6","subnet_id":"subnet-app","private_ip_address":"fd00:2::4"}]}
                    """, "\"no-op\"") + "," +
                Change("azurerm_public_ip.web", """{"id":"pip-1","ip_address":"203.0.113.40"}""", "\"no-op\"") + "," +
                Change("azurerm_subnet.dmz", """{"id":"subnet-dmz","name":"dmz","address_prefixes":["10.2.1.0/24"]}""", "\"no-op\"") + "," +
                Change("azurerm_subnet.app", """{"id":"subnet-app","name":"app","address_prefixes":["10.2.2.0/24","fd00:2::/64"]}""", "\"no-op\"")));

            var web = PropertiesOf(topology, "azurerm_linux_virtual_machine.web");
            Assert.Equal("10.2.1.4,10.2.2.4,203.0.113.40,fd00:2::4", web["ip_addresses"]);
            Assert.Equal(
                "azurerm_subnet.dmz=10.2.1.4;azurerm_subnet.app=10.2.2.4;azurerm_subnet.app=fd00:2::4",
                web["ip_addresses_by_network"]);
            Assert.Equal("10.2.2.0/24,fd00:2::/64", PropertiesOf(topology, "azurerm_subnet.app")["cidr"]);
        }

        [Fact]
        public void AwsInstance_HasItsPrivateAndPublicAddresses()
        {
            var topology = PlanTopologyParser.Parse(Plan(
                Change("aws_instance.web", """
                    {"subnet_id":"subnet-1","private_ip":"10.1.0.5","public_ip":"203.0.113.7","ipv6_addresses":["2600:1f18::5"]}
                    """, "\"no-op\"") + "," +
                Change("aws_instance.new", """{"subnet_id":"subnet-1","private_ip":"10.1.0.6"}""",
                    unknown: """{"public_ip":true,"ipv6_addresses":true}""") + "," +
                Change("aws_subnet.dmz", """{"id":"subnet-1","cidr_block":"10.1.0.0/24"}""")));

            var web = PropertiesOf(topology, "aws_instance.web");
            Assert.Equal("10.1.0.5,203.0.113.7,2600:1f18::5", web["ip_addresses"]);
            // The public address is on no subnet.
            Assert.Equal("aws_subnet.dmz=10.1.0.5;aws_subnet.dmz=2600:1f18::5", web["ip_addresses_by_network"]);
            Assert.Equal("10.1.0.6", PropertiesOf(topology, "aws_instance.new")["ip_addresses"]);
            Assert.Equal("10.1.0.0/24", PropertiesOf(topology, "aws_subnet.dmz")["cidr"]);
        }

        [Fact]
        public void ProxmoxNetworks_TakeTheirSdnSubnetsOrTheBridgeAddress()
        {
            var configuration = """
                {"resources":[{"address":"proxmox_virtual_environment_sdn_subnet.b","expressions":
                  {"vnet":{"references":["proxmox_virtual_environment_sdn_vnet.b.id","proxmox_virtual_environment_sdn_vnet.b"]}}}]}
                """;

            var topology = PlanTopologyParser.Parse(Plan(
                Change("proxmox_virtual_environment_sdn_vnet.a", """{"id":"a","zone":"z"}""") + "," +
                Change("proxmox_virtual_environment_sdn_vnet.b", """{"zone":"z"}""") + "," +
                Change("proxmox_virtual_environment_sdn_vnet.c", """{"id":"c","zone":"z"}""") + "," +
                Change("proxmox_virtual_environment_sdn_subnet.a", """{"vnet":"a","cidr":"10.0.1.0/24","gateway":"10.0.1.1"}""") + "," +
                Change("proxmox_virtual_environment_sdn_subnet.a6", """{"vnet":"a","cidr":"fd00:1::/64"}""") + "," +
                // Its vnet's id is known only after apply: found by reference.
                Change("proxmox_virtual_environment_sdn_subnet.b", """{"cidr":"10.0.2.0/24"}""") + "," +
                Change("proxmox_virtual_environment_network_linux_bridge.lab", """{"name":"vmbr5","address":"10.5.0.1/24"}""") + "," +
                Change("proxmox_network_linux_bridge.bare", """{"name":"vmbr6"}"""),
                configuration));

            Assert.Equal("10.0.1.0/24,fd00:1::/64", PropertiesOf(topology, "proxmox_virtual_environment_sdn_vnet.a")["cidr"]);
            Assert.Equal("10.0.2.0/24", PropertiesOf(topology, "proxmox_virtual_environment_sdn_vnet.b")["cidr"]);
            // A vnet with no subnet has no cidr.
            Assert.False(PropertiesOf(topology, "proxmox_virtual_environment_sdn_vnet.c").ContainsKey("cidr"));
            // The bridge's own address names its network.
            Assert.Equal("10.5.0.0/24", PropertiesOf(topology, "proxmox_virtual_environment_network_linux_bridge.lab")["cidr"]);
            Assert.False(PropertiesOf(topology, "proxmox_network_linux_bridge.bare").ContainsKey("cidr"));
            // The subnets are not drawn.
            Assert.DoesNotContain(topology.Nodes, x => x.Type == "proxmox_virtual_environment_sdn_subnet");
        }

        [Fact]
        public void UnknownOrSensitiveCidr_IsLeftOut()
        {
            var topology = PlanTopologyParser.Parse(Plan(
                Change("aws_subnet.a", """{"id":"subnet-a"}""", unknown: """{"cidr_block":true}""") + "," +
                Change("aws_subnet.b", """{"id":"subnet-b","cidr_block":"10.7.0.0/24"}""", sensitive: """{"cidr_block":true}""")));

            Assert.False(PropertiesOf(topology, "aws_subnet.a").ContainsKey("cidr"));
            Assert.False(PropertiesOf(topology, "aws_subnet.b").ContainsKey("cidr"));
            Assert.DoesNotContain("10.7.0", System.Text.Json.JsonSerializer.Serialize(topology));
        }

        [Fact]
        public void VspherePortGroups_HaveNoCidr()
        {
            Assert.All(
                Vsphere().Nodes.Where(x => x.Kind == TopologyNodeKind.Network),
                x => Assert.False(x.Properties.ContainsKey("cidr")));
        }

        [Fact]
        public void ConfiguredTypeReplacingABuiltIn_HasNoAddresses()
        {
            var options = new TopologyOptions
            {
                Machines = { ["aws_instance"] = new TopologyMachineType { Network = "subnet_id" } },
                Networks = { ["aws_subnet"] = new TopologyNetworkType { Keys = ["id"] } },
            };

            var topology = PlanTopologyParser.Parse(Plan(
                Change("aws_instance.web", """{"subnet_id":"subnet-1","private_ip":"10.1.0.5"}""") + "," +
                Change("aws_subnet.dmz", """{"id":"subnet-1","cidr_block":"10.1.0.0/24"}""")), options);

            Assert.Empty(PropertiesOf(topology, "aws_instance.web"));
            Assert.Empty(PropertiesOf(topology, "aws_subnet.dmz"));
        }

        #endregion
    }
}
