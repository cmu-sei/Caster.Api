// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Domain.Models;
using Xunit;
using Directory = Caster.Api.Domain.Models.Directory;

namespace Caster.Api.Tests.Unit.Models
{
    [Trait("Category", "Unit")]
    [Trait("Category", "Workspace")]
    public class WorkspaceTests
    {
        [Fact]
        public void IsDefault_WhenNameIsDefault_ReturnsTrue()
        {
            var workspace = new Workspace { Name = "default" };

            Assert.True(workspace.IsDefault);
        }

        [Fact]
        public void IsDefault_WhenNameIsDefaultUpperCase_ReturnsTrue()
        {
            var workspace = new Workspace { Name = "Default" };

            Assert.True(workspace.IsDefault);
        }

        [Fact]
        public void IsDefault_WhenNameIsNotDefault_ReturnsFalse()
        {
            var workspace = new Workspace { Name = "staging" };

            Assert.False(workspace.IsDefault);
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
        public void GetPath_ReturnsCorrectPath()
        {
            var id = Guid.NewGuid();
            var workspace = new Workspace { Id = id };
            var basePath = "/tmp/terraform";

            var path = workspace.GetPath(basePath);

            Assert.Equal(System.IO.Path.Combine(basePath, id.ToString()), path);
        }

        [Fact]
        public void GetStatePath_DefaultWorkspace_ReturnsBaseStatePath()
        {
            var workspace = new Workspace { Name = "default" };
            var basePath = "/tmp/terraform";

            var path = workspace.GetStatePath(basePath, backupState: false);

            Assert.Equal(System.IO.Path.Combine(basePath, "terraform.tfstate"), path);
        }

        [Fact]
        public void GetStatePath_DefaultWorkspaceBackup_ReturnsBackupPath()
        {
            var workspace = new Workspace { Name = "default" };
            var basePath = "/tmp/terraform";

            var path = workspace.GetStatePath(basePath, backupState: true);

            Assert.Equal(System.IO.Path.Combine(basePath, "terraform.tfstate.backup"), path);
        }

        [Fact]
        public void GetStatePath_NonDefaultWorkspace_ReturnsNamespacedPath()
        {
            var workspace = new Workspace { Name = "staging" };
            var basePath = "/tmp/terraform";

            var path = workspace.GetStatePath(basePath, backupState: false);

            Assert.Equal(System.IO.Path.Combine(basePath, "terraform.tfstate.d", "staging", "terraform.tfstate"), path);
        }

        [Fact]
        public void GetStatePath_NonDefaultWorkspaceBackup_ReturnsNamespacedBackupPath()
        {
            var workspace = new Workspace { Name = "staging" };
            var basePath = "/tmp/terraform";

            var path = workspace.GetStatePath(basePath, backupState: true);

            Assert.Equal(System.IO.Path.Combine(basePath, "terraform.tfstate.d", "staging", "terraform.tfstate.backup"), path);
        }

        [Fact]
        public void Constructor_WithDirectoryParam_SetsDirectoryAndId()
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
        public void GetRemovedResources_WhenBothStatesNull_ReturnsEmpty()
        {
            var workspace = new Workspace { State = null, StateBackup = null };

            var removed = workspace.GetRemovedResources();

            Assert.Empty(removed);
        }
    }
}
