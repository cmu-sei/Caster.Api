// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.Topology;
using Caster.Api.Features.Workspaces;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Serialization;
using Caster.Api.Tests.Support;
using File = System.IO.File;

namespace Caster.Api.Tests.Unit.Topology
{
    /// <summary>
    /// Which topology a workspace shows for the state of its last run. Runs over the shared database harness,
    /// since the handler reads the workspace, its runs and their plans.
    /// </summary>
    [Trait("Category", "Topology")]
    public class GetTopologyTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
    {
        private static readonly string Data = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "topology");

        private Guid _workspaceId;

        private async Task SeedWorkspace(RunStatus? status, bool applied)
        {
            var project = TestData.Project();
            var directory = TestData.Directory(project);
            var workspace = TestData.Workspace(directory, "topology");
            workspace.State = applied ? File.ReadAllText(Path.Combine(Data, "proxmox-modules.tfstate.json")) : null;
            _workspaceId = workspace.Id;
            await Seed(project, directory, workspace);

            if (status != null)
            {
                var run = TestData.Run(workspace, status.Value);
                var plan = TestData.Plan(run);
                plan.Topology = JsonSerializer.Serialize(
                    PlanTopologyParser.Parse(File.ReadAllText(Path.Combine(Data, "proxmox-modules.plan.json"))),
                    DefaultJsonSettings.Settings);
                await Seed(run, plan);
            }
        }

        private Task<WorkspaceTopology> Get() =>
            new GetTopology.Handler(Substitute.For<ICasterAuthorizationService>(), Db)
                .HandleRequest(new GetTopology.Query { Id = _workspaceId }, Ct);

        [Theory]
        [InlineData(RunStatus.Planned)]
        [InlineData(RunStatus.ApplyQueued)]
        [InlineData(RunStatus.Applying)]
        [InlineData(RunStatus.Applied_StateError)]
        public async Task PlanStillToBeApplied_ShowsWhatItWillDo(RunStatus status)
        {
            await SeedWorkspace(status, applied: false);

            var topology = await Get();

            Assert.Equal(TopologySource.Plan, topology.Source);
            Assert.Equal(13, topology.Nodes.Count);
            Assert.All(topology.Nodes.Where(x => !x.External), x => Assert.Equal("create", x.Action));
        }

        [Fact]
        public async Task Applied_ShowsTheState()
        {
            await SeedWorkspace(RunStatus.Applied, applied: true);

            var topology = await Get();

            Assert.Equal(TopologySource.State, topology.Source);
            Assert.NotNull(topology.RunId);
            Assert.Equal(13, topology.Nodes.Count);
            Assert.All(topology.Nodes, x => Assert.Null(x.Action));
        }

        [Fact]
        public async Task Rejected_ShowsTheStateWithoutThePlan()
        {
            await SeedWorkspace(RunStatus.Rejected, applied: false);

            var topology = await Get();

            Assert.Equal(TopologySource.State, topology.Source);
            Assert.Empty(topology.Nodes);
        }

        [Fact]
        public async Task Failed_ShowsWhatWasNotCreated()
        {
            await SeedWorkspace(RunStatus.Failed, applied: false);

            var topology = await Get();

            Assert.Equal(TopologySource.State, topology.Source);
            Assert.Equal(13, topology.Nodes.Count);
            Assert.All(topology.Nodes.Where(x => !x.External), x => Assert.True(x.NotCreated));
        }

        [Fact]
        public async Task NoPlan_ShowsTheState()
        {
            await SeedWorkspace(null, applied: true);

            var topology = await Get();

            Assert.Equal(TopologySource.State, topology.Source);
            Assert.Null(topology.RunId);
            Assert.Equal(13, topology.Nodes.Count);
        }
    }
}
