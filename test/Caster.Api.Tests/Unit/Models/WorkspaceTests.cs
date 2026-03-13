// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Domain.Models;
using Directory = Caster.Api.Domain.Models.Directory;
using File = Caster.Api.Domain.Models.File;
using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace Caster.Api.Tests.Unit.Models
{
    [Category("Unit")]
    [Category("Workspace")]
    public class WorkspaceTests
    {
        [Test]
        public async Task IsDefault_WhenNameIsDefault_ReturnsTrue()
        {
            var workspace = new Workspace { Name = "default" };

            await Assert.That(workspace.IsDefault).IsTrue();
        }

        [Test]
        public async Task IsDefault_WhenNameIsDefaultUpperCase_ReturnsTrue()
        {
            var workspace = new Workspace { Name = "Default" };

            await Assert.That(workspace.IsDefault).IsTrue();
        }

        [Test]
        public async Task IsDefault_WhenNameIsNotDefault_ReturnsFalse()
        {
            var workspace = new Workspace { Name = "staging" };

            await Assert.That(workspace.IsDefault).IsFalse();
        }

        [Test]
        public async Task GetState_WhenStateIsNull_ReturnsEmptyState()
        {
            var workspace = new Workspace { State = null };

            var state = workspace.GetState();

            await Assert.That(state).IsNotNull();
        }

        [Test]
        public async Task GetStateBackup_WhenStateBackupIsNull_ReturnsEmptyState()
        {
            var workspace = new Workspace { StateBackup = null };

            var stateBackup = workspace.GetStateBackup();

            await Assert.That(stateBackup).IsNotNull();
        }

        [Test]
        public async Task GetPath_ReturnsCorrectPath()
        {
            var id = Guid.NewGuid();
            var workspace = new Workspace { Id = id };
            var basePath = "/tmp/terraform";

            var path = workspace.GetPath(basePath);

            await Assert.That(path).IsEqualTo(System.IO.Path.Combine(basePath, id.ToString()));
        }

        [Test]
        public async Task GetStatePath_DefaultWorkspace_ReturnsBaseStatePath()
        {
            var workspace = new Workspace { Name = "default" };
            var basePath = "/tmp/terraform";

            var path = workspace.GetStatePath(basePath, backupState: false);

            await Assert.That(path).IsEqualTo(System.IO.Path.Combine(basePath, "terraform.tfstate"));
        }

        [Test]
        public async Task GetStatePath_DefaultWorkspaceBackup_ReturnsBackupPath()
        {
            var workspace = new Workspace { Name = "default" };
            var basePath = "/tmp/terraform";

            var path = workspace.GetStatePath(basePath, backupState: true);

            await Assert.That(path).IsEqualTo(System.IO.Path.Combine(basePath, "terraform.tfstate.backup"));
        }

        [Test]
        public async Task GetStatePath_NonDefaultWorkspace_ReturnsNamespacedPath()
        {
            var workspace = new Workspace { Name = "staging" };
            var basePath = "/tmp/terraform";

            var path = workspace.GetStatePath(basePath, backupState: false);

            await Assert.That(path).IsEqualTo(System.IO.Path.Combine(basePath, "terraform.tfstate.d", "staging", "terraform.tfstate"));
        }

        [Test]
        public async Task GetStatePath_NonDefaultWorkspaceBackup_ReturnsNamespacedBackupPath()
        {
            var workspace = new Workspace { Name = "staging" };
            var basePath = "/tmp/terraform";

            var path = workspace.GetStatePath(basePath, backupState: true);

            await Assert.That(path).IsEqualTo(System.IO.Path.Combine(basePath, "terraform.tfstate.d", "staging", "terraform.tfstate.backup"));
        }

        [Test]
        public async Task Constructor_WithDirectoryParam_SetsDirectoryAndId()
        {
            var directory = new Directory { Id = Guid.NewGuid(), Name = "TestDir" };

            var workspace = new Workspace("test-workspace", directory);

            await Assert.That(workspace.Name).IsEqualTo("test-workspace");
            await Assert.That(workspace.DirectoryId).IsEqualTo(directory.Id);
            await Assert.That(workspace.Directory).IsEqualTo(directory);
        }

        [Test]
        public async Task Constructor_WithNullDirectory_DoesNotThrow()
        {
            var workspace = new Workspace("test-workspace", null);

            await Assert.That(workspace.Name).IsEqualTo("test-workspace");
        }

        [Test]
        public async Task GetRemovedResources_WhenBothStatesNull_ReturnsEmpty()
        {
            var workspace = new Workspace { State = null, StateBackup = null };

            var removed = workspace.GetRemovedResources();

            await Assert.That(removed).IsEmpty();
        }
    }
}
