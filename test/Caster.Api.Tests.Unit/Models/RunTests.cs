// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Domain.Models;
using TUnit.Core;

namespace Caster.Api.Tests.Unit.Models
{
    [Category("Unit")]
    public class RunTests
    {
        [Test]
        public async Task Constructor_WhenCreatingNewRun_SetsDefaultStatusToQueued()
        {
            var run = new Run();

            await Assert.That(run.Status).IsEqualTo(RunStatus.Queued);
        }

        [Test]
        public async Task Constructor_WhenCreatingNewRun_SetsCreatedAtToCurrentTime()
        {
            var before = DateTime.UtcNow;
            var run = new Run();
            var after = DateTime.UtcNow;

            await Assert.That(run.CreatedAt).IsGreaterThanOrEqualTo(before);
            await Assert.That(run.CreatedAt).IsLessThanOrEqualTo(after);
        }

        [Test]
        public async Task Modify_WhenCalled_UpdatesModifiedAtAndModifiedById()
        {
            var run = new Run();
            var userId = Guid.NewGuid();
            var before = DateTime.UtcNow;

            run.Modify(userId);

            await Assert.That(run.ModifiedById).IsEqualTo(userId);
            await Assert.That(run.ModifiedAt).IsNotNull();
            await Assert.That(run.ModifiedAt.Value).IsGreaterThanOrEqualTo(before);
        }

        [Test]
        public async Task GetActiveStatuses_WhenCalled_ContainsExpectedStatuses()
        {
            var activeStatuses = RunHelpers.GetActiveStatuses();

            await Assert.That(activeStatuses).Contains(RunStatus.Queued);
            await Assert.That(activeStatuses).Contains(RunStatus.Planning);
            await Assert.That(activeStatuses).Contains(RunStatus.Applying);
        }

        [Test]
        public async Task GetActiveStatuses_WhenCalled_DoesNotContainTerminalStatuses()
        {
            var activeStatuses = RunHelpers.GetActiveStatuses();

            await Assert.That(activeStatuses).DoesNotContain(RunStatus.Applied);
            await Assert.That(activeStatuses).DoesNotContain(RunStatus.Failed);
            await Assert.That(activeStatuses).DoesNotContain(RunStatus.Rejected);
            await Assert.That(activeStatuses).DoesNotContain(RunStatus.Planned);
        }

        [Test]
        public async Task GetActiveStatuses_WhenCalled_ReturnsExactly3Statuses()
        {
            var activeStatuses = RunHelpers.GetActiveStatuses();

            await Assert.That(activeStatuses.Count).IsEqualTo(3);
        }
    }
}
