// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Domain.Models;
using Xunit;

namespace Caster.Api.Tests.Unit.Models
{
    [Trait("Category", "Unit")]
    public class WorkspaceTests
    {
        [Theory]
        [InlineData("default", true)]
        [InlineData("Default", true)]
        [InlineData("staging", false)]
        public void IsDefault_WithVariousNames_ReturnsExpectedResult(string name, bool expectedIsDefault)
        {
            var workspace = new Workspace { Name = name };

            Assert.Equal(expectedIsDefault, workspace.IsDefault);
        }

        [Fact]
        public void GetState_WhenStateIsNull_ReturnsEmptyState()
        {
            var workspace = new Workspace { State = null };

            var state = workspace.GetState();

            Assert.NotNull(state);
        }

        [Fact]
        public void GetStateBackup_WhenStateBackupIsNull_ReturnsEmptyState()
        {
            var workspace = new Workspace { StateBackup = null };

            var stateBackup = workspace.GetStateBackup();

            Assert.NotNull(stateBackup);
        }

        [Fact]
        public void GetPath_WithBasePath_ReturnsCorrectPath()
        {
            var id = Guid.NewGuid();
            var workspace = new Workspace { Id = id };
            var basePath = "/tmp/terraform";

            var path = workspace.GetPath(basePath);

            Assert.Equal(System.IO.Path.Combine(basePath, id.ToString()), path);
        }

        [Theory]
        [InlineData("default", false, "terraform.tfstate")]
        [InlineData("default", true, "terraform.tfstate.backup")]
        public void GetStatePath_WithDefaultWorkspace_ReturnsExpectedPath(string workspaceName, bool backupState, string expectedFileName)
        {
            var workspace = new Workspace { Name = workspaceName };
            var basePath = "/tmp/terraform";

            var path = workspace.GetStatePath(basePath, backupState: backupState);

            Assert.Equal(System.IO.Path.Combine(basePath, expectedFileName), path);
        }

        [Theory]
        [InlineData("staging", false, "terraform.tfstate")]
        [InlineData("staging", true, "terraform.tfstate.backup")]
        public void GetStatePath_WithNonDefaultWorkspace_ReturnsNamespacedPath(string workspaceName, bool backupState, string expectedFileName)
        {
            var workspace = new Workspace { Name = workspaceName };
            var basePath = "/tmp/terraform";

            var path = workspace.GetStatePath(basePath, backupState: backupState);

            Assert.Equal(System.IO.Path.Combine(basePath, "terraform.tfstate.d", workspaceName, expectedFileName), path);
        }

        [Fact]
        public void Constructor_WithDirectoryParameter_SetsDirectoryAndId()
        {
            var directory = new Directory { Id = Guid.NewGuid(), Name = "TestDir" };

            var workspace = new Workspace("test-workspace", directory);

            Assert.Equal("test-workspace", workspace.Name);
            Assert.Equal(directory.Id, workspace.DirectoryId);
            Assert.Equal(directory, workspace.Directory);
        }

        [Fact]
        public void Constructor_WithNullDirectory_DoesNotThrow()
        {
            var workspace = new Workspace("test-workspace", null);

            Assert.Equal("test-workspace", workspace.Name);
        }

        [Fact]
        public void GetRemovedResources_WhenBothStatesNull_ReturnsEmptyCollection()
        {
            var workspace = new Workspace { State = null, StateBackup = null };

            var removed = workspace.GetRemovedResources();

            Assert.Empty(removed);
        }
    }
}
