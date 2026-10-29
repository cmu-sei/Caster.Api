// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Designs;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Tests.Support;
using Xunit;

namespace Caster.Api.Tests.Features.Designs
{
    [Trait("Category", "Unit")]
    [Trait("Category", "Designs")]
    public class AiDesignDraftBuilderTests
    {
        private static Module Module(string path, params string[] providers)
        {
            var module = TestData.Module(path.Split('/').Last());
            module.Path = path;
            var version = TestData.ModuleVersion(module);
            version.Providers = providers;
            module.Versions.Add(version);
            return module;
        }

        private static readonly ModuleSourceOptions[] Sources =
        [
            new() { Name = "crucible-modules", Url = "https://git.test/modules.git" },
            new() { Name = "ssh-only", Url = "ssh://git.test/modules.git", AvailableToRuns = false },
        ];

        [Fact]
        public void SelectCatalog_Leaves_Out_Sources_Runs_Cannot_Download()
        {
            var modules = new[] { Module("crucible-modules/vm"), Module("ssh-only/vm") };

            var catalog = AiDesignDraftBuilder.SelectCatalog(modules, Sources, null, null);

            Assert.Equal(["crucible-modules"], catalog.Select(c => c.Source));
        }

        [Fact]
        public void SelectCatalog_Keeps_Target_And_Generic_Modules()
        {
            var modules = new[]
            {
                Module("crucible-modules/proxmox-vm", "bpg/proxmox"),
                Module("crucible-modules/vsphere-vm", "vmware/vsphere"),
                Module("crucible-modules/password", "hashicorp/random"),
                Module("crucible-modules/unread", null),
            };

            var catalog = AiDesignDraftBuilder.SelectCatalog(modules, Sources, null, [Hypervisors.Proxmox]);

            Assert.Equal(["password", "proxmox-vm", "unread"], catalog.Select(c => c.Module.Name));
            Assert.Equal([null, Hypervisors.Proxmox, null], catalog.Select(c => c.Hypervisor));
        }

        [Fact]
        public void SelectCatalog_With_No_Target_Offers_Every_Hypervisor()
        {
            var modules = new[] { Module("crucible-modules/a", "bpg/proxmox"), Module("crucible-modules/b", "vmware/vsphere") };

            Assert.Equal(2, AiDesignDraftBuilder.SelectCatalog(modules, Sources, null, null).Count);
        }

        [Fact]
        public void SelectCatalog_Unmatched_Path_Takes_The_Fallback_Source()
        {
            var catalog = AiDesignDraftBuilder.SelectCatalog([Module("group/vm")], Sources, "gitlab", null);

            Assert.Equal("gitlab", catalog.Single().Source);
        }

        [Fact]
        public void ResolveHypervisors_Defaults_To_Every_Configured()
        {
            var warnings = new List<string>();

            Assert.Equal(Hypervisors.All, AiDesignDraftBuilder.ResolveHypervisors(Hypervisors.All, [], warnings));
            Assert.Equal([Hypervisors.VSphere], AiDesignDraftBuilder.ResolveHypervisors(Hypervisors.All, ["vsphere"], warnings));
            Assert.Empty(warnings);
        }

        [Fact]
        public void ResolveHypervisors_Refuses_One_That_Is_Not_Configured()
        {
            Assert.Throws<ConflictException>(() =>
                AiDesignDraftBuilder.ResolveHypervisors([Hypervisors.Proxmox], [Hypervisors.VSphere], []));
        }

        [Fact]
        public void ResolveHypervisors_With_None_Configured_Offers_All_And_Warns()
        {
            var warnings = new List<string>();

            Assert.Null(AiDesignDraftBuilder.ResolveHypervisors([], null, warnings));
            Assert.Single(warnings);
        }

        [Fact]
        public void Review_Warns_About_A_Reference_Across_Hypervisors()
        {
            var network = Module("crucible-modules/proxmox-network", "bpg/proxmox");
            network.Versions.Single().Outputs.Add(new ModuleOutput { Name = "id" });
            var vm = Module("crucible-modules/vsphere-vm", "vmware/vsphere");
            vm.Versions.Single().Variables.Add(new ModuleVariable { Name = "network", VariableType = "string" });
            var catalog = AiDesignDraftBuilder.SelectCatalog([network, vm], Sources, null, null);

            using var input = JsonDocument.Parse($$"""
                {
                  "designName": "mixed",
                  "modules": [
                    { "moduleId": "{{network.Id}}", "name": "lan", "values": {} },
                    { "moduleId": "{{vm.Id}}", "name": "web", "values": { "network": "${module.lan.id}" } }
                  ]
                }
                """);

            var draft = AiDesignDraftBuilder.Review(input.RootElement, catalog, null);

            Assert.Equal([Hypervisors.Proxmox, Hypervisors.VSphere], draft.Modules.Select(m => m.Hypervisor));
            Assert.Equal(["crucible-modules", "crucible-modules"], draft.Modules.Select(m => m.Source));
            Assert.Contains(draft.Warnings, w => w.StartsWith("web.network references module.lan.id, but web builds on vSphere and 'lan' on Proxmox."));
        }

        [Fact]
        public void Review_Gives_A_Required_Value_Left_Out_A_Placeholder()
        {
            var vm = Module("crucible-modules/proxmox-vm", "bpg/proxmox");
            vm.Versions.Single().Variables.AddRange(
            [
                new ModuleVariable { Name = "name", VariableType = "string" },
                new ModuleVariable { Name = "template_name", VariableType = "string" },
                new ModuleVariable { Name = "node_name", VariableType = "string" },
            ]);
            var catalog = AiDesignDraftBuilder.SelectCatalog([vm], Sources, null, null);

            using var input = JsonDocument.Parse($$"""
                {
                  "designName": "domain",
                  "modules": [
                    { "moduleId": "{{vm.Id}}", "name": "dc01", "values": { "name": "dc01", "template_name": "REPLACE_WITH_DC_TEMPLATE" } }
                  ]
                }
                """);

            var draft = AiDesignDraftBuilder.Review(input.RootElement, catalog, null);

            var dc = draft.Modules.Single();
            Assert.Equal(["node_name"], dc.MissingRequiredVariables);
            Assert.Equal("REPLACE_WITH_NODE_NAME", dc.Values.Single(v => v.Name == "node_name").Value);
            Assert.Equal(["template_name", "node_name"], dc.Placeholders);
            Assert.Contains("dc01: replace the placeholders in template_name, node_name before enabling the design.", draft.Warnings);
        }

        [Fact]
        public void BuildSystemPrompt_Asks_For_Placeholders_Instead_Of_Guessed_Names()
        {
            var catalog = AiDesignDraftBuilder.SelectCatalog([Module("crucible-modules/vm", "bpg/proxmox")], Sources, null, null);

            var prompt = AiDesignDraftBuilder.BuildSystemPrompt(catalog);

            Assert.Contains("Never guess a name that has to exist in the range", prompt);
            Assert.Contains("REPLACE_WITH_DC_TEMPLATE", prompt);
        }

        [Fact]
        public void HypervisorProblems_Allows_Generic_Modules_Either_Way()
        {
            var problems = AiDesignDraftBuilder.HypervisorProblems(
            [
                ("pw", null, [new ModuleValue { Name = "x", Value = "${module.vm.id}" }]),
                ("vm", Hypervisors.Proxmox, [new ModuleValue { Name = "password", Value = "${module.pw.result}" }]),
            ]);

            Assert.Empty(problems);
        }

        [Fact]
        public void Review_Checks_Template_And_Network_Values_Against_The_Inventory()
        {
            var vm = Module("crucible-modules/proxmox-vm", "bpg/proxmox");
            vm.Versions.Single().Variables.AddRange(
            [
                new ModuleVariable { Name = "template_id", VariableType = "number" },
                new ModuleVariable { Name = "bridge", VariableType = "string" },
                new ModuleVariable { Name = "datastore_id", VariableType = "string", DefaultValue = "local-lvm" },
            ]);
            var network = Module("crucible-modules/proxmox-network", "bpg/proxmox");
            network.Versions.Single().Variables.Add(new ModuleVariable { Name = "vnet_name", VariableType = "string" });
            network.Versions.Single().Outputs.Add(new ModuleOutput { Name = "vnet_id" });
            var catalog = AiDesignDraftBuilder.SelectCatalog([vm, network], Sources, null, [Hypervisors.Proxmox]);
            var inventory = AiInventory.Load(AiInventoryTests.Service(("default", InfrastructureProviders.Proxmox, AiInventoryTests.Proxmox())), [Hypervisors.Proxmox]);

            using var input = JsonDocument.Parse($$"""
                {
                  "designName": "lab",
                  "modules": [
                    { "moduleId": "{{network.Id}}", "name": "dmz", "values": { "vnet_name": "dmz" } },
                    { "moduleId": "{{vm.Id}}", "name": "web", "values": { "template_id": "9000", "bridge": "dmz", "datastore_id": "REPLACE_WITH_DATASTORE" } },
                    { "moduleId": "{{vm.Id}}", "name": "db", "values": { "template_id": "9100", "bridge": "${module.dmz.vnet_id}" } }
                  ]
                }
                """);

            var draft = AiDesignDraftBuilder.Review(input.RootElement, catalog, null, inventory, [Hypervisors.Proxmox]);

            Assert.Equal(["9000"], draft.Inventory.Found.Select(f => f.Name));
            var unknown = Assert.Single(draft.Inventory.Unknown);
            Assert.Equal(("9100", "db.template_id"), (unknown.Name, unknown.Where));
            Assert.Contains(draft.Warnings, w => w.StartsWith("db.template_id: template vmid 9100 is not in the Proxmox inventory"));
            Assert.Contains(AiTopology.NoPlan, draft.Warnings);
        }

        [Fact]
        public void BuildSystemPrompt_Carries_The_Topology_Guidance_And_The_Inventory()
        {
            var catalog = AiDesignDraftBuilder.SelectCatalog([Module("crucible-modules/vm", "bpg/proxmox")], Sources, null, null);

            var prompt = AiDesignDraftBuilder.BuildSystemPrompt(catalog, null, "Infrastructure inventory: listed here.\n");

            Assert.Contains("Range topology:", prompt);
            Assert.Contains("Infrastructure inventory: listed here.", prompt);
            Assert.Contains(AiTopology.ToolProperty, AiDesignDraftBuilder.BuildToolSchema(catalog).ToJsonString());
        }

        [Fact]
        public void BuildSystemPrompt_Explains_Each_Hypervisor_Keeps_Its_Own_Networks()
        {
            var catalog = AiDesignDraftBuilder.SelectCatalog([Module("crucible-modules/vm", "bpg/proxmox")], Sources, null, Hypervisors.All);

            var prompt = AiDesignDraftBuilder.BuildSystemPrompt(catalog, Hypervisors.All);

            Assert.Contains("spans Proxmox and vSphere", prompt);
            Assert.Contains("hypervisor Proxmox)", prompt);
        }
    }
}
