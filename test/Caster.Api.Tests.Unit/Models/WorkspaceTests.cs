// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Domain.Models;
using TUnit.Core;

namespace Caster.Api.Tests.Unit.Models
{
    [Category("Unit")]
    public class WorkspaceTests
    {
        [Test]
        [Arguments("default", true)]
        [Arguments("Default", true)]
        [Arguments("staging", false)]
        public async Task IsDefault_WithVariousNames_ReturnsExpectedResult(string name, bool expectedIsDefault)
        {
            var workspace = new Workspace { Name = name };

            await Assert.That(workspace.IsDefault).IsEqualTo(expectedIsDefault);
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
        public async Task GetPath_WithBasePath_ReturnsCorrectPath()
        {
            var id = Guid.NewGuid();
            var workspace = new Workspace { Id = id };
            var basePath = "/tmp/terraform";

            var path = workspace.GetPath(basePath);

            await Assert.That(path).IsEqualTo(System.IO.Path.Combine(basePath, id.ToString()));
        }

        [Test]
        [Arguments("default", false, "terraform.tfstate")]
        [Arguments("default", true, "terraform.tfstate.backup")]
        public async Task GetStatePath_WithDefaultWorkspace_ReturnsExpectedPath(string workspaceName, bool backupState, string expectedFileName)
        {
            var workspace = new Workspace { Name = workspaceName };
            var basePath = "/tmp/terraform";

            var path = workspace.GetStatePath(basePath, backupState: backupState);

            await Assert.That(path).IsEqualTo(System.IO.Path.Combine(basePath, expectedFileName));
        }

        [Test]
        [Arguments("staging", false, "terraform.tfstate")]
        [Arguments("staging", true, "terraform.tfstate.backup")]
        public async Task GetStatePath_WithNonDefaultWorkspace_ReturnsNamespacedPath(string workspaceName, bool backupState, string expectedFileName)
        {
            var workspace = new Workspace { Name = workspaceName };
            var basePath = "/tmp/terraform";

            var path = workspace.GetStatePath(basePath, backupState: backupState);

            await Assert.That(path).IsEqualTo(System.IO.Path.Combine(basePath, "terraform.tfstate.d", workspaceName, expectedFileName));
        }

        [Test]
        public async Task Constructor_WithDirectoryParameter_SetsDirectoryAndId()
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
        public async Task GetRemovedResources_WhenBothStatesNull_ReturnsEmptyCollection()
        {
            var workspace = new Workspace { State = null, StateBackup = null };

            var removed = workspace.GetRemovedResources();

            await Assert.That(removed).IsEmpty();
        }
    }
}
