// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using Caster.Api.Domain.Events;
using Caster.Api.Domain.Services;
using Caster.Api.Hubs;
using Caster.Api.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Caster.Api.Tests.Domain.Services
{
    /// <summary>
    /// The queue <see cref="RunQueueService"/> keeps of plans and applies waiting for a slot, driven directly
    /// without starting the service: nothing here executes a run.
    /// </summary>
    public class RunQueueServiceTests
    {
        private readonly RunQueueService _sut = new(
            Substitute.For<IServiceProvider>(),
            new RecordingLogger<RunQueueService>(),
            Options.Create(new TerraformOptions { MaxConcurrentRuns = 1 }),
            new HubRecorder<ProjectHub>());

        [Fact]
        public void A_queued_plan_appears_in_the_queue_positions()
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
        public void A_queued_apply_appears_in_the_queue_positions()
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
        public void Queued_runs_are_numbered_in_order_of_arrival()
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
        public void An_apply_is_queued_ahead_of_an_earlier_plan()
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

        [Fact]
        public void GetQueuePosition_answers_the_position_of_a_queued_run()
        {
            var runId = Guid.NewGuid();
            _sut.Add(new RunAdded { RunId = runId, WorkspaceId = Guid.NewGuid() });

            var position = _sut.GetQueuePosition(runId);
            Assert.NotNull(position);
            Assert.Equal(runId, position.RunId);
            Assert.Equal(1, position.Position);
        }

        [Fact]
        public void GetQueuePosition_answers_null_for_a_run_that_is_not_queued()
        {
            _sut.Add(new RunAdded { RunId = Guid.NewGuid(), WorkspaceId = Guid.NewGuid() });

            var position = _sut.GetQueuePosition(Guid.NewGuid());
            Assert.Null(position);
        }

        [Fact]
        public void Cancel_removes_the_run_and_renumbers_the_rest()
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
        public void Cancel_of_an_unknown_run_leaves_the_queue_as_it_was()
        {
            var runId = Guid.NewGuid();
            _sut.Add(new RunAdded { RunId = runId, WorkspaceId = Guid.NewGuid() });

            _sut.Cancel(Guid.NewGuid());

            var positions = _sut.GetQueuePositions();
            Assert.Single(positions);
            Assert.Equal(runId, positions[0].RunId);
        }

        [Fact]
        public void Cancel_reduces_the_total()
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
        public void Cancel_removes_a_queued_apply()
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
    }
}
