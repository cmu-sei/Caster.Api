// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.Topology;
using Caster.Api.Features.Workspaces;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Serialization;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;
using File = System.IO.File;

namespace Caster.Api.Tests.Unit.Topology
{
    [Trait("Category", "Unit")]
    [Trait("Category", "Topology")]
    public class GetTopologyTests : IDisposable
    {
        private static readonly string Data = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "topology");

        private readonly CasterContext _db;
        private readonly GetTopology.Handler _sut;
        private readonly Guid _workspaceId = Guid.NewGuid();

        public GetTopologyTests()
        {
            _db = new CasterContext(new DbContextOptionsBuilder<CasterContext>()
                .UseInMemoryDatabase($"caster_topology_{Guid.NewGuid():N}")
                .Options);
            _sut = new GetTopology.Handler(Substitute.For<ICasterAuthorizationService>(), _db);
        }

        public void Dispose() => _db.Dispose();

        private void Seed(RunStatus? status, bool applied)
        {
            _db.Workspaces.Add(new Domain.Models.Workspace
            {
                Id = _workspaceId,
                Name = "topology",
                State = applied ? File.ReadAllText(Path.Combine(Data, "proxmox-modules.tfstate.json")) : null,
            });

            if (status != null)
            {
                var plan = PlanTopologyParser.Parse(File.ReadAllText(Path.Combine(Data, "proxmox-modules.plan.json")));
                var run = new Run { Id = Guid.NewGuid(), WorkspaceId = _workspaceId, Status = status.Value };
                run.Plan = new Plan
                {
                    Id = Guid.NewGuid(),
                    RunId = run.Id,
                    Topology = JsonSerializer.Serialize(plan, DefaultJsonSettings.Settings),
                };
                _db.Runs.Add(run);
            }

            _db.SaveChanges();
        }

        private Task<WorkspaceTopology> Get() =>
            _sut.HandleRequest(new GetTopology.Query { Id = _workspaceId }, CancellationToken.None);

        [Theory]
        [InlineData(RunStatus.Planned)]
        [InlineData(RunStatus.ApplyQueued)]
        [InlineData(RunStatus.Applying)]
        [InlineData(RunStatus.Applied_StateError)]
        public async Task PlanStillToBeApplied_ShowsWhatItWillDo(RunStatus status)
        {
            Seed(status, applied: false);

            var topology = await Get();

            Assert.Equal(TopologySource.Plan, topology.Source);
            Assert.Equal(13, topology.Nodes.Count);
            Assert.All(topology.Nodes.Where(x => !x.External), x => Assert.Equal("create", x.Action));
        }

        [Fact]
        public async Task Applied_ShowsTheState()
        {
            Seed(RunStatus.Applied, applied: true);

            var topology = await Get();

            Assert.Equal(TopologySource.State, topology.Source);
            Assert.NotNull(topology.RunId);
            Assert.Equal(13, topology.Nodes.Count);
            Assert.All(topology.Nodes, x => Assert.Null(x.Action));
        }

        [Fact]
        public async Task Rejected_ShowsTheStateWithoutThePlan()
        {
            Seed(RunStatus.Rejected, applied: false);

            var topology = await Get();

            Assert.Equal(TopologySource.State, topology.Source);
            Assert.Empty(topology.Nodes);
        }

        [Fact]
        public async Task Failed_ShowsWhatWasNotCreated()
        {
            Seed(RunStatus.Failed, applied: false);

            var topology = await Get();

            Assert.Equal(TopologySource.State, topology.Source);
            Assert.Equal(13, topology.Nodes.Count);
            Assert.All(topology.Nodes.Where(x => !x.External), x => Assert.True(x.NotCreated));
        }

        [Fact]
        public async Task NoPlan_ShowsTheState()
        {
            Seed(null, applied: true);

            var topology = await Get();

            Assert.Equal(TopologySource.State, topology.Source);
            Assert.Null(topology.RunId);
            Assert.Equal(13, topology.Nodes.Count);
        }
    }
}
