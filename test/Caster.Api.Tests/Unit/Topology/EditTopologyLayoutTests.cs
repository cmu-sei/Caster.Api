// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Workspaces;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Tests.Support;

namespace Caster.Api.Tests.Unit.Topology
{
    /// <summary>
    /// What the topology view saves on a workspace: each layout's positions apart, and a role shared by both.
    /// </summary>
    [Trait("Category", "Topology")]
    public class EditTopologyLayoutTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
    {
        private const string Node = "module.lan.proxmox_virtual_environment_vm.vm";

        private Guid _workspaceId;

        private async Task SeedWorkspace()
        {
            var project = TestData.Project();
            var directory = TestData.Directory(project);
            var workspace = TestData.Workspace(directory, "topology");
            _workspaceId = workspace.Id;
            await Seed(project, directory, workspace);
        }

        private Task<Dictionary<string, TopologyNodeLayout>> Edit(TopologyNodeLayout node) =>
            new EditTopologyLayout.Handler(Substitute.For<ICasterAuthorizationService>(), Db)
                .HandleRequest(new EditTopologyLayout.Command { Id = _workspaceId, Nodes = { [Node] = node } }, Ct);

        [Fact]
        public async Task TreeAndForcePositions_AreKeptApart()
        {
            await SeedWorkspace();
            await Edit(new TopologyNodeLayout { X = 10, Y = 20, Role = TopologyNodeRole.Router });

            var layout = await Edit(new TopologyNodeLayout { X = 10, Y = 20, TreeX = 300, TreeY = 400, Role = TopologyNodeRole.Router });

            var node = layout[Node];
            Assert.Equal(10, node.X);
            Assert.Equal(20, node.Y);
            Assert.Equal(300, node.TreeX);
            Assert.Equal(400, node.TreeY);
            Assert.Equal(TopologyNodeRole.Router, node.Role);
        }

        [Fact]
        public async Task OnlyATreePosition_IsStillRemembered()
        {
            await SeedWorkspace();

            var layout = await Edit(new TopologyNodeLayout { TreeX = 300, TreeY = 400 });

            Assert.True(layout.ContainsKey(Node));
            Assert.Equal(300, EditTopologyLayout.Read((await Db.Workspaces.FindAsync([_workspaceId], Ct)).TopologyLayout)[Node].TreeX);
        }

        [Fact]
        public async Task NothingLeftToRemember_ForgetsTheNode()
        {
            await SeedWorkspace();
            await Edit(new TopologyNodeLayout { TreeX = 300, TreeY = 400 });

            var layout = await Edit(new TopologyNodeLayout());

            Assert.Empty(layout);
            Assert.Null((await Db.Workspaces.FindAsync([_workspaceId], Ct)).TopologyLayout);
        }

        [Fact]
        public void ATreePositionThatIsNotFinite_IsRejected()
        {
            var command = new EditTopologyLayout.Command { Nodes = { [Node] = new TopologyNodeLayout { TreeX = double.NaN, TreeY = 1 } } };

            Assert.False(new EditTopologyLayout.CommandValidator().Validate(command).IsValid);
        }
    }
}
