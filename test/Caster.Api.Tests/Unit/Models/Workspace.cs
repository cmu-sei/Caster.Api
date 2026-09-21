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
        public void Test_GetStatePath_Rejects_Traversing_Workspace_Names()
        {
            var workspace = new Workspace() { Id = Guid.NewGuid(), Name = "../../escaped" };

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
