// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Domain.Models;
using Xunit;

namespace Caster.Api.Tests.Unit.Models
{
    [Trait("Category", "Unit")]
    public class RunTests
    {
        [Fact]
        public void Constructor_WhenCreatingNewRun_SetsDefaultStatusToQueued()
        {
            var run = new Run();

            Assert.Equal(RunStatus.Queued, run.Status);
        }

        [Fact]
        public void Constructor_WhenCreatingNewRun_SetsCreatedAtToCurrentTime()
        {
            var before = DateTime.UtcNow;
            var run = new Run();
            var after = DateTime.UtcNow;

            Assert.InRange(run.CreatedAt, before, after);
        }

        [Fact]
        public void Modify_WhenCalled_UpdatesModifiedAtAndModifiedById()
        {
            var run = new Run();
            var userId = Guid.NewGuid();
            var before = DateTime.UtcNow;

            run.Modify(userId);

            Assert.Equal(userId, run.ModifiedById);
            Assert.NotNull(run.ModifiedAt);
            Assert.True(run.ModifiedAt >= before);
        }

        [Fact]
        public void GetActiveStatuses_WhenCalled_ContainsExpectedStatuses()
        {
            var activeStatuses = RunHelpers.GetActiveStatuses();

            Assert.Contains(RunStatus.Queued, activeStatuses);
            Assert.Contains(RunStatus.Planning, activeStatuses);
            Assert.Contains(RunStatus.Applying, activeStatuses);
        }

        [Fact]
        public void GetActiveStatuses_WhenCalled_DoesNotContainTerminalStatuses()
        {
            var activeStatuses = RunHelpers.GetActiveStatuses();

            Assert.DoesNotContain(RunStatus.Applied, activeStatuses);
            Assert.DoesNotContain(RunStatus.Failed, activeStatuses);
            Assert.DoesNotContain(RunStatus.Rejected, activeStatuses);
            Assert.DoesNotContain(RunStatus.Planned, activeStatuses);
        }

        [Fact]
        public void GetActiveStatuses_WhenCalled_ReturnsExactly3Statuses()
        {
            var activeStatuses = RunHelpers.GetActiveStatuses();

            Assert.Equal(3, activeStatuses.Count);
        }
    }
}
