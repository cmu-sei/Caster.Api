// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using Caster.Api.Domain.Events;
using Caster.Api.Domain.Services;
using Caster.Api.Hubs;
using Caster.Api.Infrastructure.Options;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Caster.Api.Tests.Unit.Services
{
    [Trait("Category", "Unit")]
    public class RunQueueServiceTests
    {
        private readonly RunQueueService _sut;

        public RunQueueServiceTests()
        {
            var logger = Substitute.For<ILogger<RunQueueService>>();
            var options = Options.Create(new TerraformOptions { MaxConcurrentRuns = 1 });
            var hubContext = Substitute.For<IHubContext<ProjectHub>>();
            var serviceProvider = Substitute.For<IServiceProvider>();

            _sut = new RunQueueService(serviceProvider, logger, options, hubContext);
        }

        #region Add & GetQueuePositions

        [Fact]
        public void Add_Plan_AppearsInQueuePositions()
        {
            var runId = Guid.NewGuid();
            var workspaceId = Guid.NewGuid();

            _sut.Add(new RunAdded { RunId = runId, WorkspaceId = workspaceId });

            var positions = _sut.GetQueuePositions();
            Assert.Single(positions);
            Assert.Equal(runId, positions[0].RunId);
            Assert.Equal(workspaceId, positions[0].WorkspaceId);
            Assert.Equal(1, positions[0].Position);
            Assert.Equal(1, positions[0].Total);
        }

        [Fact]
        public void Add_Apply_AppearsInQueuePositions()
        {
            var runId = Guid.NewGuid();
            var applyId = Guid.NewGuid();
            var workspaceId = Guid.NewGuid();

            _sut.Add(new ApplyAdded { ApplyId = applyId, RunId = runId, WorkspaceId = workspaceId });

            var positions = _sut.GetQueuePositions();
            Assert.Single(positions);
            Assert.Equal(runId, positions[0].RunId);
            Assert.Equal(workspaceId, positions[0].WorkspaceId);
        }

        [Fact]
        public void Add_Multiple_PositionsAreSequential()
        {
            _sut.Add(new RunAdded { RunId = Guid.NewGuid(), WorkspaceId = Guid.NewGuid() });
            _sut.Add(new RunAdded { RunId = Guid.NewGuid(), WorkspaceId = Guid.NewGuid() });
            _sut.Add(new RunAdded { RunId = Guid.NewGuid(), WorkspaceId = Guid.NewGuid() });

            var positions = _sut.GetQueuePositions();
            Assert.Equal(3, positions.Count);
            Assert.Equal(1, positions[0].Position);
            Assert.Equal(2, positions[1].Position);
            Assert.Equal(3, positions[2].Position);
            Assert.All(positions, p => Assert.Equal(3, p.Total));
        }

        [Fact]
        public void Add_AppliesHavePriorityOverPlans()
        {
            var planId = Guid.NewGuid();
            var applyRunId = Guid.NewGuid();

            _sut.Add(new RunAdded { RunId = planId, WorkspaceId = Guid.NewGuid() });
            _sut.Add(new ApplyAdded { ApplyId = Guid.NewGuid(), RunId = applyRunId, WorkspaceId = Guid.NewGuid() });

            var positions = _sut.GetQueuePositions();
            Assert.Equal(2, positions.Count);
            Assert.Equal(applyRunId, positions[0].RunId);
            Assert.Equal(1, positions[0].Position);
            Assert.Equal(planId, positions[1].RunId);
            Assert.Equal(2, positions[1].Position);
        }

        #endregion

        #region GetQueuePosition (single)

        [Fact]
        public void GetQueuePosition_ExistingRun_ReturnsPosition()
        {
            var runId = Guid.NewGuid();
            _sut.Add(new RunAdded { RunId = runId, WorkspaceId = Guid.NewGuid() });

            var position = _sut.GetQueuePosition(runId);
            Assert.NotNull(position);
            Assert.Equal(runId, position.RunId);
            Assert.Equal(1, position.Position);
        }

        [Fact]
        public void GetQueuePosition_NonExistent_ReturnsNull()
        {
            _sut.Add(new RunAdded { RunId = Guid.NewGuid(), WorkspaceId = Guid.NewGuid() });

            var position = _sut.GetQueuePosition(Guid.NewGuid());
            Assert.Null(position);
        }

        #endregion

        #region Cancel

        [Fact]
        public void Cancel_RemovesFromQueuePositions()
        {
            var runId1 = Guid.NewGuid();
            var runId2 = Guid.NewGuid();

            _sut.Add(new RunAdded { RunId = runId1, WorkspaceId = Guid.NewGuid() });
            _sut.Add(new RunAdded { RunId = runId2, WorkspaceId = Guid.NewGuid() });

            _sut.Cancel(runId1);

            var positions = _sut.GetQueuePositions();
            Assert.Single(positions);
            Assert.Equal(runId2, positions[0].RunId);
            Assert.Equal(1, positions[0].Position);
        }

        [Fact]
        public void Cancel_UnknownId_DoesNotAffectExistingItems()
        {
            var runId = Guid.NewGuid();
            _sut.Add(new RunAdded { RunId = runId, WorkspaceId = Guid.NewGuid() });

            _sut.Cancel(Guid.NewGuid());

            var positions = _sut.GetQueuePositions();
            Assert.Single(positions);
            Assert.Equal(runId, positions[0].RunId);
        }

        [Fact]
        public void Cancel_UpdatesTotal()
        {
            _sut.Add(new RunAdded { RunId = Guid.NewGuid(), WorkspaceId = Guid.NewGuid() });
            var cancelId = Guid.NewGuid();
            _sut.Add(new RunAdded { RunId = cancelId, WorkspaceId = Guid.NewGuid() });
            _sut.Add(new RunAdded { RunId = Guid.NewGuid(), WorkspaceId = Guid.NewGuid() });

            _sut.Cancel(cancelId);

            var positions = _sut.GetQueuePositions();
            Assert.Equal(2, positions.Count);
            Assert.All(positions, p => Assert.Equal(2, p.Total));
        }

        [Fact]
        public void Cancel_Apply_RemovesFromQueuePositions()
        {
            var applyRunId = Guid.NewGuid();
            var planRunId = Guid.NewGuid();

            _sut.Add(new ApplyAdded { ApplyId = Guid.NewGuid(), RunId = applyRunId, WorkspaceId = Guid.NewGuid() });
            _sut.Add(new RunAdded { RunId = planRunId, WorkspaceId = Guid.NewGuid() });

            _sut.Cancel(applyRunId);

            var positions = _sut.GetQueuePositions();
            Assert.Single(positions);
            Assert.Equal(planRunId, positions[0].RunId);
        }

        #endregion
    }
}
