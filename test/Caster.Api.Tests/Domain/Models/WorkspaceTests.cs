// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;

namespace Caster.Api.Tests.Domain.Models
{
    /// <summary>
    /// <see cref="Workspace"/>'s file-system helpers, which write a workspace's files and find its state under
    /// Terraform's working directory and must never reach outside it.
    /// </summary>
    public class WorkspaceTests : IDisposable
    {
        private readonly string _basePath;

        public WorkspaceTests()
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
        public async Task PrepareFileSystem_writes_the_files_into_the_working_directory()
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
        public async Task PrepareFileSystem_refuses_a_file_name_that_climbs_out_of_the_working_directory(string fileName)
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
        public async Task PrepareFileSystem_refuses_a_rooted_file_name()
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
        public async Task PrepareFileSystem_refuses_a_sibling_directory_whose_name_shares_the_prefix()
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
        public async Task PrepareFileSystem_writes_percent_encoded_names_literally()
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
        public async Task PrepareFileSystem_accepts_a_working_directory_with_a_trailing_separator()
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
        public void GetStatePath_accepts_a_base_path_with_a_trailing_separator()
        {
            var workspace = new Workspace() { Id = Guid.NewGuid(), Name = "my-workspace" };

            var statePath = workspace.GetStatePath(_basePath + Path.DirectorySeparatorChar, backupState: false);

            Assert.Equal(
                Path.Combine(_basePath, "terraform.tfstate.d", "my-workspace", "terraform.tfstate"),
                statePath);
        }

        [Fact]
        public void GetStatePath_accepts_the_root_directory_as_the_base_path()
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
        public void GetStatePath_refuses_a_workspace_name_that_climbs_out_of_the_base_path(string name)
        {
            var workspace = new Workspace() { Id = Guid.NewGuid(), Name = name };

            Assert.Throws<InvalidOperationException>(() => workspace.GetStatePath(_basePath, backupState: false));
        }

        /// <summary>
        /// Traversal that resolves back inside the working directory is not an escape, so it is
        /// allowed. The guarantee is containment, not that state stays under terraform.tfstate.d.
        /// </summary>
        [Fact]
        public void GetStatePath_allows_traversal_that_resolves_back_inside_the_base_path()
        {
            var workspace = new Workspace() { Id = Guid.NewGuid(), Name = "subdir/../../escaped" };

            var statePath = workspace.GetStatePath(_basePath, backupState: false);

            Assert.StartsWith(
                _basePath + Path.DirectorySeparatorChar,
                Path.GetFullPath(statePath),
                StringComparison.Ordinal);
        }

        [Fact]
        public void GetStatePath_refuses_a_rooted_workspace_name()
        {
            var workspace = new Workspace()
            {
                Id = Guid.NewGuid(),
                Name = Path.Combine(Path.GetPathRoot(Path.GetTempPath()), "escaped")
            };

            Assert.Throws<InvalidOperationException>(() => workspace.GetStatePath(_basePath, backupState: false));
        }

        [Fact]
        public void GetStatePath_puts_the_state_under_terraform_tfstate_d()
        {
            var workspace = new Workspace() { Id = Guid.NewGuid(), Name = "my-workspace" };

            var statePath = workspace.GetStatePath(_basePath, backupState: false);

            Assert.Equal(
                Path.Combine(_basePath, "terraform.tfstate.d", "my-workspace", "terraform.tfstate"),
                statePath);
        }
    }
}
