// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Xunit;

namespace Caster.Api.Tests.Unit
{
    [Trait("Category", "Unit")]
    [Trait("Category", "Workspace")]
    public class WorkspaceUnitTest : IDisposable
    {
        private readonly string _basePath;

        public WorkspaceUnitTest()
        {
            _basePath = Path.Combine(Path.GetTempPath(), $"caster-test-{Guid.NewGuid()}");
            System.IO.Directory.CreateDirectory(_basePath);
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(_basePath))
            {
                System.IO.Directory.Delete(_basePath, true);
            }
        }

        [Fact]
        public async Task Test_PrepareFileSystem_Writes_Files()
        {
            var workspace = new Workspace() { Id = Guid.NewGuid(), Name = "default" };
            var workingDir = workspace.GetPath(_basePath);
            var files = new List<Caster.Api.Domain.Models.File>
            {
                new() { Name = "main.tf", Content = "content" }
            };

            await workspace.PrepareFileSystem(workingDir, files);

            Assert.True(System.IO.File.Exists(Path.Combine(workingDir, "main.tf")));
        }

        [Theory]
        [InlineData("../escaped.tf")]
        [InlineData("../../etc/cron.d/backdoor")]
        [InlineData("subdir/../../escaped.tf")]
        public async Task Test_PrepareFileSystem_Rejects_Traversing_File_Names(string fileName)
        {
            var workspace = new Workspace() { Id = Guid.NewGuid(), Name = "default" };
            var workingDir = workspace.GetPath(_basePath);
            var files = new List<Caster.Api.Domain.Models.File>
            {
                new() { Name = fileName, Content = "content" }
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.PrepareFileSystem(workingDir, files));
            Assert.False(System.IO.File.Exists(Path.Combine(_basePath, "escaped.tf")));
        }

        [Fact]
        public async Task Test_PrepareFileSystem_Rejects_Rooted_File_Names()
        {
            var workspace = new Workspace() { Id = Guid.NewGuid(), Name = "default" };
            var workingDir = workspace.GetPath(_basePath);
            var outsidePath = Path.Combine(Path.GetTempPath(), $"caster-test-outside-{Guid.NewGuid()}.tf");
            var files = new List<Caster.Api.Domain.Models.File>
            {
                new() { Name = outsidePath, Content = "content" }
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.PrepareFileSystem(workingDir, files));
            Assert.False(System.IO.File.Exists(outsidePath));
        }

        [Fact]
        public async Task Test_PrepareFileSystem_Rejects_Sibling_Directories_With_A_Matching_Prefix()
        {
            var workspace = new Workspace() { Id = Guid.NewGuid(), Name = "default" };
            var workingDir = Path.Combine(_basePath, "workspace");
            var files = new List<Caster.Api.Domain.Models.File>
            {
                new() { Name = Path.Combine("..", "workspace-other", "main.tf"), Content = "content" }
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.PrepareFileSystem(workingDir, files));
            Assert.False(System.IO.Directory.Exists(Path.Combine(_basePath, "workspace-other")));
        }

        [Fact]
        public async Task Test_PrepareFileSystem_Does_Not_Decode_Percent_Encoded_File_Names()
        {
            var workspace = new Workspace() { Id = Guid.NewGuid(), Name = "default" };
            var workingDir = workspace.GetPath(_basePath);
            var files = new List<Caster.Api.Domain.Models.File>
            {
                new() { Name = "%2e%2e%2fescaped.tf", Content = "content" }
            };

            await workspace.PrepareFileSystem(workingDir, files);

            Assert.True(System.IO.File.Exists(Path.Combine(workingDir, "%2e%2e%2fescaped.tf")));
            Assert.False(System.IO.File.Exists(Path.Combine(_basePath, "escaped.tf")));
        }

        [Fact]
        public async Task Test_PrepareFileSystem_Allows_A_Working_Directory_With_A_Trailing_Separator()
        {
            var workspace = new Workspace() { Id = Guid.NewGuid(), Name = "default" };
            var workingDir = workspace.GetPath(_basePath) + Path.DirectorySeparatorChar;
            var files = new List<Caster.Api.Domain.Models.File>
            {
                new() { Name = "main.tf", Content = "content" }
            };

            await workspace.PrepareFileSystem(workingDir, files);

            Assert.True(System.IO.File.Exists(Path.Combine(workingDir, "main.tf")));
        }

        [Fact]
        public void Test_GetStatePath_Allows_A_Base_Path_With_A_Trailing_Separator()
        {
            var workspace = new Workspace() { Id = Guid.NewGuid(), Name = "my-workspace" };

            var statePath = workspace.GetStatePath(_basePath + Path.DirectorySeparatorChar, backupState: false);

            Assert.Equal(
                Path.Combine(_basePath, "terraform.tfstate.d", "my-workspace", "terraform.tfstate"),
                statePath);
        }

        [Fact]
        public void Test_GetStatePath_Allows_The_Root_Directory_As_A_Base_Path()
        {
            var root = Path.GetPathRoot(Path.GetTempPath());
            var workspace = new Workspace() { Id = Guid.NewGuid(), Name = "my-workspace" };

            var statePath = workspace.GetStatePath(root, backupState: false);

            Assert.Equal(
                Path.Combine(root, "terraform.tfstate.d", "my-workspace", "terraform.tfstate"),
                statePath);
        }

        [Theory]
        [InlineData("../../escaped")]
        [InlineData("../../../../../../../../etc/cron.d")]
        public void Test_GetStatePath_Rejects_Traversing_Workspace_Names(string name)
        {
            var workspace = new Workspace() { Id = Guid.NewGuid(), Name = name };

            Assert.Throws<InvalidOperationException>(() => workspace.GetStatePath(_basePath, backupState: false));
        }

        /// <summary>
        /// Traversal that resolves back inside the working directory is not an escape, so it is
        /// allowed. The guarantee is containment, not that state stays under terraform.tfstate.d.
        /// </summary>
        [Fact]
        public void Test_GetStatePath_Contains_Traversal_That_Resolves_Back_Inside()
        {
            var workspace = new Workspace() { Id = Guid.NewGuid(), Name = "subdir/../../escaped" };

            var statePath = workspace.GetStatePath(_basePath, backupState: false);

            Assert.StartsWith(
                _basePath + Path.DirectorySeparatorChar,
                Path.GetFullPath(statePath),
                StringComparison.Ordinal);
        }

        [Fact]
        public void Test_GetStatePath_Rejects_Rooted_Workspace_Names()
        {
            var workspace = new Workspace()
            {
                Id = Guid.NewGuid(),
                Name = Path.Combine(Path.GetPathRoot(Path.GetTempPath()), "escaped")
            };

            Assert.Throws<InvalidOperationException>(() => workspace.GetStatePath(_basePath, backupState: false));
        }

        [Fact]
        public void Test_GetStatePath_Allows_Valid_Workspace_Names()
        {
            var workspace = new Workspace() { Id = Guid.NewGuid(), Name = "my-workspace" };

            var statePath = workspace.GetStatePath(_basePath, backupState: false);

            Assert.Equal(
                Path.Combine(_basePath, "terraform.tfstate.d", "my-workspace", "terraform.tfstate"),
                statePath);
        }
    }
}
