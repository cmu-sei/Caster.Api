// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Services.Modules;
using Caster.Api.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Caster.Api.Tests.Unit.Modules
{
    /// <summary>
    /// Drives the real git binary against a real bare repository served over
    /// file://, which needs no credentials and so isolates the driver from the
    /// credential path. Builds the repository from the Crucible catalog's own
    /// variables.tf.json / outputs.tf.json / caster.json in a monorepo layout,
    /// which is what the catalog actually is today.
    /// </summary>
    /// <remarks>
    /// git is a hard dependency of the Caster image (installed in the
    /// Dockerfile alongside credential.helper=store), so requiring it here
    /// matches production.
    /// </remarks>
    [Trait("Category", "Unit")]
    [Trait("Category", "ModuleSources")]
    public class GitModuleSourceEndToEndTests : IDisposable
    {
        private static readonly string CatalogData =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "catalog");

        private readonly string _scratch;
        private readonly string _bareRepositoryUrl;
        private readonly CasterContext _db;
        private readonly GitModuleRepositoryProvider _sut;

        public GitModuleSourceEndToEndTests()
        {
            _scratch = Path.Combine(Path.GetTempPath(), $"caster-e2e-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_scratch);

            _bareRepositoryUrl = BuildBareRepository();

            var builder = new DbContextOptionsBuilder<CasterContext>();
            builder.UseInMemoryDatabase($"caster_e2e_{Guid.NewGuid():N}");
            _db = new CasterContext(builder.Options);

            var monitor = Substitute.For<IOptionsMonitor<TerraformOptions>>();
            monitor.CurrentValue.Returns(new TerraformOptions { ModuleSourceTimeoutSeconds = 60 });

            _sut = new GitModuleRepositoryProvider(
                _db,
                new GitCommandRunner(monitor, Substitute.For<ILogger<GitCommandRunner>>()),
                Substitute.For<ILogger<GitModuleRepositoryProvider>>());
        }

        public void Dispose()
        {
            _db.Dispose();

            if (Directory.Exists(_scratch))
            {
                ClearReadOnly(_scratch);
                Directory.Delete(_scratch, recursive: true);
            }
        }

        private ModuleSourceOptions Source() => new()
        {
            Name = "crucible-modules",
            Description = "Crucible vSphere module catalog",
            Provider = ModuleSourceProviders.Git,
            Url = _bareRepositoryUrl,
            Layout = ModuleSourceLayout.Subdirectories,
        };

        #region Acceptance

        [Fact]
        public async Task ModulesAreDiscoveredFromTagsOverFileUrlWithNoGitlabAnywhere()
        {
            await _sut.SyncModulesAsync(Source(), false, CancellationToken.None);

            var modules = await _db.Modules
                .Include(x => x.Versions)
                .OrderBy(x => x.Path)
                .ToListAsync();

            Assert.Equal(2, modules.Count);
            Assert.Equal(
                ["crucible-modules/machine-blank", "crucible-modules/network-segment"],
                modules.Select(x => x.Path));

            // v0.1.0 is a lightweight tag and v0.2.0 an annotated one; both are
            // selectable, and no release API exists on a file:// remote.
            foreach (var module in modules)
            {
                Assert.Equal(["v0.1.0", "v0.2.0"], module.Versions.Select(x => x.Name).OrderBy(x => x));
            }
        }

        [Fact]
        public async Task VariablesAndOutputsArePopulated()
        {
            await _sut.SyncModulesAsync(Source(), false, CancellationToken.None);

            var segment = await _db.Modules
                .Include(x => x.Versions)
                .SingleAsync(x => x.Path == "crucible-modules/network-segment");

            var version = segment.Versions.Single(x => x.Name == "v0.1.0");

            Assert.Equal(8, version.Variables.Count);
            Assert.Equal(4, version.Outputs.Count);
            Assert.Contains(version.Variables, x => x.Name == "distributed_switch" && !x.IsOptional);
            Assert.Contains(version.Outputs, x => x.Name == "vlan_id");
        }

        [Fact]
        public async Task SnippetTargetsTheTagOverPlainGit()
        {
            await _sut.SyncModulesAsync(Source(), false, CancellationToken.None);

            var segment = await _db.Modules
                .Include(x => x.Versions)
                .SingleAsync(x => x.Path == "crucible-modules/network-segment");

            var snippet = segment.Versions
                .Single(x => x.Name == "v0.1.0")
                .ToSnippet("red_lan", [new Caster.Api.Domain.Models.ModuleValue { Name = "name", Value = "red-lan" }]);

            Assert.Contains($"source = \"git::{_bareRepositoryUrl}//network-segment?ref=v0.1.0\"", snippet);

            // The address terraform init would resolve, proven by asking git to
            // fetch exactly that ref from exactly that url.
            Assert.Equal(0, RunGit(_scratch, "ls-remote", "--exit-code", _bareRepositoryUrl, "refs/tags/v0.1.0"));
        }

        [Fact]
        public async Task VersionDateComesFromTheTaggedCommit()
        {
            await _sut.SyncModulesAsync(Source(), false, CancellationToken.None);

            var version = await _db.ModuleVersions.FirstAsync();

            Assert.NotEqual(default, version.DateCreated);
            Assert.True(version.DateCreated < DateTime.UtcNow.AddMinutes(1));
        }

        [Fact]
        public async Task TempDirectoriesAreGoneAfterASuccessfulSync()
        {
            var root = Path.Combine(Path.GetTempPath(), GitModuleRepositoryProvider.TempDirectoryName);
            var before = Directory.Exists(root) ? Directory.GetDirectories(root).Length : 0;

            await _sut.SyncModulesAsync(Source(), false, CancellationToken.None);

            var after = Directory.Exists(root) ? Directory.GetDirectories(root).Length : 0;

            Assert.Equal(before, after);
        }

        [Fact]
        public async Task TempDirectoriesAreGoneAfterAFailedClone()
        {
            var root = Path.Combine(Path.GetTempPath(), GitModuleRepositoryProvider.TempDirectoryName);
            var before = Directory.Exists(root) ? Directory.GetDirectories(root).Length : 0;

            var source = Source();
            source.Url = _bareRepositoryUrl;

            // Tags resolve, then the clone is asked for a ref that is not there.
            var broken = new GitModuleRepositoryProvider(
                _db,
                new FailingCloneRunner(_bareRepositoryUrl),
                Substitute.For<ILogger<GitModuleRepositoryProvider>>());

            await Assert.ThrowsAnyAsync<Exception>(() =>
                broken.SyncModulesAsync(source, false, CancellationToken.None));

            var after = Directory.Exists(root) ? Directory.GetDirectories(root).Length : 0;

            Assert.Equal(before, after);
        }

        [Fact]
        public async Task MissingRemote_FailsWithAReadableMessageAndNoHang()
        {
            var source = Source();
            source.Url = "file:///" + Path.Combine(_scratch, "does-not-exist.git").TrimStart('/');

            var exception = await Assert.ThrowsAsync<GitCommandException>(() =>
                _sut.SyncModulesAsync(source, false, CancellationToken.None));

            Assert.Contains("Could not list tags for", exception.Message);
        }

        #endregion

        #region Credential hygiene

        [Theory]
        [InlineData(
            "https://git-access-token:supersecret@gitlab.local/terraform-modules.git",
            "https://***@gitlab.local/terraform-modules.git")]
        [InlineData(
            "fatal: could not read from https://user:pw@host/x.git",
            "fatal: could not read from https://***@host/x.git")]
        [InlineData("file:///srv/git/catalog.git", "file:///srv/git/catalog.git")]
        [InlineData("https://gitlab.local/terraform-modules.git", "https://gitlab.local/terraform-modules.git")]
        public void Redact_RemovesUserinfoAndNothingElse(string input, string expected)
        {
            Assert.Equal(expected, GitCommandRunner.Redact(input));
        }

        [Fact]
        public async Task AFailureAgainstACredentialBearingUrlNeverEchoesTheCredential()
        {
            var source = Source();
            source.Url = "https://git-access-token:supersecret@127.0.0.1:1/nope.git";

            var exception = await Assert.ThrowsAsync<GitCommandException>(() =>
                _sut.SyncModulesAsync(source, false, CancellationToken.None));

            Assert.DoesNotContain("supersecret", exception.Message);
            Assert.Contains("***@", exception.Message);
        }

        #endregion

        #region Fixture

        /// <summary>
        /// Lays out a two-module monorepo from the real catalog files, commits
        /// it, tags it twice (one lightweight, one annotated) and returns a
        /// file:// url to a bare clone of it.
        /// </summary>
        private string BuildBareRepository()
        {
            var work = Path.Combine(_scratch, "catalog");
            Directory.CreateDirectory(work);

            foreach (var module in new[] { "network-segment", "machine-blank" })
            {
                var directory = Path.Combine(work, module);
                Directory.CreateDirectory(directory);

                foreach (var file in new[] { "variables.tf.json", "outputs.tf.json", "caster.json" })
                {
                    File.Copy(Path.Combine(CatalogData, module, file), Path.Combine(directory, file));
                }

                File.WriteAllText(Path.Combine(directory, "main.tf"), "# vsphere resources\n");

                // The hcl/ mirror the catalog carries, which must not be
                // mistaken for a module root.
                var hcl = Path.Combine(directory, "hcl");
                Directory.CreateDirectory(hcl);
                File.WriteAllText(Path.Combine(hcl, "variables.tf"), "variable \"name\" { type = string }\n");
            }

            var tools = Path.Combine(work, "tools");
            Directory.CreateDirectory(tools);
            File.WriteAllText(Path.Combine(tools, "generate.mjs"), "// generator\n");

            File.WriteAllText(Path.Combine(work, "CATALOG.md"), "# Crucible Terraform Module Catalog\n");

            RunGitOrThrow(work, "init", "-q", "-b", "main");
            RunGitOrThrow(work, "config", "user.name", "Caster Test");
            RunGitOrThrow(work, "config", "user.email", "test@localhost");
            RunGitOrThrow(work, "config", "commit.gpgsign", "false");
            RunGitOrThrow(work, "config", "tag.gpgsign", "false");
            RunGitOrThrow(work, "add", "-A");
            RunGitOrThrow(work, "commit", "-q", "-m", "catalog");
            // Lightweight.
            RunGitOrThrow(work, "tag", "v0.1.0");
            // Annotated, so ls-remote emits the peeled ^{} line too.
            RunGitOrThrow(work, "tag", "-a", "v0.2.0", "-m", "release v0.2.0");

            var bare = Path.Combine(_scratch, "catalog.git");
            RunGitOrThrow(_scratch, "clone", "--bare", "-q", work, bare);

            return "file:///" + bare.Replace('\\', '/').TrimStart('/');
        }

        private static void RunGitOrThrow(string workingDirectory, params string[] arguments)
        {
            var exitCode = RunGit(workingDirectory, arguments);

            if (exitCode != 0)
            {
                throw new InvalidOperationException(
                    $"git {string.Join(" ", arguments)} failed with exit code {exitCode}.");
            }
        }

        private static int RunGit(string workingDirectory, params string[] arguments)
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
            // file:// clones of a local path are refused by newer git without this.
            startInfo.Environment["GIT_ALLOW_PROTOCOL"] = "file:git:https:ssh";

            using var process = Process.Start(startInfo);
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();

            return process.ExitCode;
        }

        private static void ClearReadOnly(string path)
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                var attributes = File.GetAttributes(file);

                if (attributes.HasFlag(FileAttributes.ReadOnly))
                {
                    File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                }
            }
        }

        /// <summary>
        /// Real tag discovery, then a clone of a tag that does not exist, so the
        /// cleanup path runs after git has already created the destination.
        /// </summary>
        private class FailingCloneRunner(string url) : IGitCommandRunner
        {
            private readonly GitCommandRunner _inner = new(
                BuildMonitor(),
                Substitute.For<ILogger<GitCommandRunner>>());

            private static IOptionsMonitor<TerraformOptions> BuildMonitor()
            {
                var monitor = Substitute.For<IOptionsMonitor<TerraformOptions>>();
                monitor.CurrentValue.Returns(new TerraformOptions { ModuleSourceTimeoutSeconds = 60 });
                return monitor;
            }

            public Task<System.Collections.Generic.IReadOnlyList<string>> ListRemoteTagsAsync(
                string requestedUrl, CancellationToken cancellationToken) =>
                _inner.ListRemoteTagsAsync(url, cancellationToken);

            public Task CloneTagAsync(string requestedUrl, string tag, string destinationPath, CancellationToken cancellationToken) =>
                _inner.CloneTagAsync(url, "refs/tags/no-such-tag", destinationPath, cancellationToken);

            public Task<DateTime?> GetHeadCommitDateAsync(string repositoryPath, CancellationToken cancellationToken) =>
                _inner.GetHeadCommitDateAsync(repositoryPath, cancellationToken);
        }

        #endregion
    }
}
