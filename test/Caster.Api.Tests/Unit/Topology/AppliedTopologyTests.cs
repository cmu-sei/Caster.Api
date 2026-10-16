// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.Topology;
using Xunit;
using File = System.IO.File;

namespace Caster.Api.Tests.Unit.Topology
{
    /// <summary>
    /// Runs against the state Terraform would write after applying the Proxmox
    /// plan in full (Data/topology/proxmox-modules.tfstate.json, built from the
    /// plan's planned_values), and against that state with machines removed, as
    /// a failed apply leaves it.
    /// </summary>
    [Trait("Category", "Unit")]
    [Trait("Category", "Topology")]
    public class AppliedTopologyTests
    {
        private static readonly string Data = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "topology");

        private const string Router = "module.fw_edge.proxmox_virtual_environment_vm.vm";
        private const string Web = "module.host[\"web\"].proxmox_virtual_environment_vm.vm";

        private static string State() => File.ReadAllText(Path.Combine(Data, "proxmox-modules.tfstate.json"));

        private static Domain.Models.Topology Plan() =>
            PlanTopologyParser.Parse(File.ReadAllText(Path.Combine(Data, "proxmox-modules.plan.json")));

        /// <summary>The state with the router and the web server never built.</summary>
        private static string PartialState()
        {
            var state = JsonNode.Parse(State());
            var resources = state["resources"].AsArray();

            foreach (var resource in resources.Where(x =>
                (string)x["type"] == "proxmox_virtual_environment_vm" &&
                (string)x["module"] is "module.fw_edge" or "module.host[\"web\"]").ToList())
            {
                resources.Remove(resource);
            }

            return state.ToJsonString();
        }

        [Fact]
        public void ParseState_AFullApplyDrawsWhatThePlanDid()
        {
            var plan = Plan();
            var state = PlanTopologyParser.ParseState(State());

            Assert.Equal(plan.Nodes.Select(x => x.Id), state.Nodes.Select(x => x.Id));
            Assert.Equal(
                plan.Edges.Select(x => (x.From, x.Nic, x.To)).OrderBy(x => x),
                state.Edges.Select(x => (x.From, x.Nic, x.To)).OrderBy(x => x));
            Assert.Equal("fw-edge-demo", state.Nodes.Single(x => x.Id == Router).Label);
        }

        [Fact]
        public void ParseState_NothingIsAboutToChange()
        {
            var state = PlanTopologyParser.ParseState(State());

            Assert.All(state.Nodes, x => Assert.Null(x.Action));
            Assert.DoesNotContain(state.Nodes, x => x.NotCreated);
        }

        [Fact]
        public void ParseState_BuildsAddressesWithStringIndexes()
        {
            var state = PlanTopologyParser.ParseState(State());

            Assert.Contains(state.Nodes, x => x.Id == "module.network.proxmox_sdn_vnet.segment[\"dmz\"]");
            Assert.Contains(state.Nodes, x => x.Id == Web);
        }

        [Fact]
        public void Read_FailedApplyShowsWhatWasNotCreated()
        {
            var topology = AppliedTopology.Read(PartialState(), Plan(), applyFailed: true);

            Assert.Equal(13, topology.Nodes.Count);
            Assert.Equal([Router, Web], topology.Nodes.Where(x => x.NotCreated).Select(x => x.Id).Order());
            Assert.All(topology.Nodes, x => Assert.Null(x.Action));

            // Still drawn on the networks it would have been on.
            Assert.Equal(4, topology.Edges.Count(x => x.From == Router));
            Assert.Single(topology.Edges, x => x.From == Web);
        }

        [Fact]
        public void Read_SucceededOrRejectedShowsOnlyTheState()
        {
            var topology = AppliedTopology.Read(PartialState(), Plan(), applyFailed: false);

            Assert.Equal(11, topology.Nodes.Count);
            Assert.DoesNotContain(topology.Nodes, x => x.Id == Router);
            Assert.DoesNotContain(topology.Edges, x => x.From == Router);
        }

        [Fact]
        public void Read_ApplyThatFailedBeforeBuildingAnything()
        {
            var plan = Plan();
            var topology = AppliedTopology.Read(null, plan, applyFailed: true);

            Assert.Equal(plan.Nodes.Count, topology.Nodes.Count);
            Assert.All(topology.Nodes, x => Assert.True(x.NotCreated));
            Assert.Equal(plan.Edges.Count, topology.Edges.Count);
        }

        [Fact]
        public void Read_NoState()
        {
            var topology = AppliedTopology.Read(null, null, applyFailed: false);

            Assert.Empty(topology.Nodes);
            Assert.Empty(topology.Edges);
        }
    }
}
