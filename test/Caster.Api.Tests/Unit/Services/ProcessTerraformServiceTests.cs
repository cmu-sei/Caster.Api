// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.IO;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Domain.Services.Terraform;
using Caster.Api.Infrastructure.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Caster.Api.Tests.Unit.Services
{
    [Trait("Category", "Unit")]
    [Trait("Category", "Terraform")]
    public class ProcessTerraformServiceTests : IDisposable
    {
        private const string InstalledVersion = "1.5.7";

        private readonly string _rootPath;
        private readonly string _binaryPath;
        private readonly TerraformOptions _options;
        private readonly ProcessTerraformService _sut;

        public ProcessTerraformServiceTests()
        {
            // BinaryPath is nested so that "../" escapes land in directories that exist
            _rootPath = Path.Combine(Path.GetTempPath(), $"caster-tests-{Guid.NewGuid()}");
            _binaryPath = Path.Combine(_rootPath, "binaries");
            System.IO.Directory.CreateDirectory(Path.Combine(_binaryPath, InstalledVersion));
            System.IO.Directory.CreateDirectory(Path.Combine(_rootPath, "outside"));

            _options = new TerraformOptions
            {
                BinaryPath = _binaryPath,
                DefaultVersion = InstalledVersion,
                RootWorkingDirectory = _rootPath
            };

            _sut = new ProcessTerraformService(
                _options,
                Substitute.For<ILogger<ProcessTerraformService>>(),
                new MemoryCache(new MemoryCacheOptions()),
                Substitute.For<IRegexService>());
        }

        public void Dispose()
        {
            System.IO.Directory.Delete(_rootPath, recursive: true);
        }

        [Fact]
        public void Test_IsValidVersion_Accepts_Installed_Version()
        {
            Assert.True(_sut.IsValidVersion(InstalledVersion));
        }

        [Theory]
        [InlineData("..")]
        [InlineData(".")]
        [InlineData("../outside")]
        [InlineData("../..")]
        [InlineData("../../usr/bin")]
        [InlineData("1.5.7/..")]
        [InlineData("1.5.7/../1.5.7")]
        [InlineData("1.5.7/")]
        [InlineData("./1.5.7")]
        [InlineData("/")]
        [InlineData("/usr/bin")]
        [InlineData("0.0.0-not-installed")]
        [InlineData("")]
        [InlineData(null)]
        public void Test_IsValidVersion_Rejects_Paths_And_Unknown_Versions(string version)
        {
            Assert.False(_sut.IsValidVersion(version));
        }

        [Fact]
        public void Test_IsValidVersion_Rejects_Rooted_Path_To_Existing_Directory()
        {
            Assert.False(_sut.IsValidVersion(Path.Combine(_rootPath, "outside")));
        }

        [Fact]
        public void Test_IsValidVersion_Rejects_When_BinaryPath_Missing()
        {
            _options.BinaryPath = Path.Combine(_rootPath, "missing");

            Assert.False(_sut.IsValidVersion(InstalledVersion));
        }

        [Theory]
        [InlineData("../outside")]
        [InlineData("../../usr/bin")]
        [InlineData("/usr/bin")]
        public async Task Test_Run_Rejects_Workspace_Version_Before_Starting_Process(string version)
        {
            var workspace = new Workspace { Id = Guid.NewGuid(), TerraformVersion = version };

            var ex = await Assert.ThrowsAsync<ArgumentException>(() => _sut.Init(workspace, null));

            Assert.Equal("Unauthorized version specified.", ex.Message);
        }

        [Fact]
        public async Task Test_Run_Rejects_Invalid_Default_Version()
        {
            _options.DefaultVersion = "../outside";
            var workspace = new Workspace { Id = Guid.NewGuid(), TerraformVersion = null };

            await Assert.ThrowsAsync<ArgumentException>(() => _sut.Init(workspace, null));
        }
    }
}
