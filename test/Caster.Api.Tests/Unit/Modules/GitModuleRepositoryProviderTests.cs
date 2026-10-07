// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Services.Modules;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Caster.Api.Tests.Unit.Modules
{
    /// <summary>
    /// Exercises the plain-git provider against the real Crucible module
    /// catalog's variables.tf.json / outputs.tf.json / caster.json, copied into
    /// Data/catalog. Hand-written JSON is avoided on purpose: the loader's four
    /// quirks (object form only, no "default": null, no object(...) types, one
    /// block type per file) make invented fixtures misleading.
    /// </summary>
    [Trait("Category", "Unit")]
    [Trait("Category", "ModuleSources")]
    public class GitModuleRepositoryProviderTests : IDisposable
    {
        private static readonly string CatalogData =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "catalog");

        private readonly CasterContext _db;
        private readonly FakeGitCommandRunner _git = new();
        private readonly GitModuleRepositoryProvider _sut;
        private readonly string _scratch;

        public GitModuleRepositoryProviderTests()
        {
            var builder = new DbContextOptionsBuilder<CasterContext>();
            builder.UseInMemoryDatabase($"caster_modules_{Guid.NewGuid():N}");
            _db = new CasterContext(builder.Options);

            _sut = new GitModuleRepositoryProvider(
                _db,
                _git,
                Substitute.For<ILogger<GitModuleRepositoryProvider>>());

            _scratch = Path.Combine(Path.GetTempPath(), $"caster-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_scratch);
        }

        public void Dispose()
        {
            _db.Dispose();

            if (Directory.Exists(_scratch))
            {
                Directory.Delete(_scratch, recursive: true);
            }
        }

        #region Helpers

        /// <summary>A repo whose root is one module: network-segment's files at the top level.</summary>
        private string BuildRootLayoutTree()
        {
            var tree = Path.Combine(_scratch, "root-layout");
            Directory.CreateDirectory(tree);
            CopyModule("network-segment", tree);
            File.WriteAllText(Path.Combine(tree, "main.tf"), "# resources\n");
            return tree;
        }

        /// <summary>
        /// A monorepo: two module subdirectories, each with its own hcl/ mirror,
        /// plus a tools/ directory that is not a module.
        /// </summary>
        private string BuildMonorepoTree()
        {
            var tree = Path.Combine(_scratch, "monorepo");
            Directory.CreateDirectory(tree);

            foreach (var module in new[] { "network-segment", "machine-blank" })
            {
                var directory = Path.Combine(tree, module);
                Directory.CreateDirectory(directory);
                CopyModule(module, directory);
                File.WriteAllText(Path.Combine(directory, "main.tf"), "# resources\n");

                // The catalog keeps HCL in hcl/ because variables.tf and
                // variables.tf.json cannot coexist in a module root. It must
                // not be discovered as a module.
                var hcl = Path.Combine(directory, "hcl");
                Directory.CreateDirectory(hcl);
                File.WriteAllText(Path.Combine(hcl, "variables.tf"), "variable \"name\" {}\n");
            }

            var tools = Path.Combine(tree, "tools");
            Directory.CreateDirectory(tools);
            File.WriteAllText(Path.Combine(tools, "generate.mjs"), "// not a module\n");

            File.WriteAllText(Path.Combine(tree, "CATALOG.md"), "# catalog\n");

            return tree;
        }

        private static void CopyModule(string module, string destination)
        {
            foreach (var file in new[] { "variables.tf.json", "outputs.tf.json", "caster.json" })
            {
                File.Copy(Path.Combine(CatalogData, module, file), Path.Combine(destination, file));
            }
        }

        private static ModuleSourceOptions Source(ModuleSourceLayout layout, string url = "file:///srv/git/catalog.git") =>
            new()
            {
                Name = "catalog",
                Description = "source level description",
                Provider = ModuleSourceProviders.Git,
                Url = url,
                Layout = layout,
            };

        private void GivenTags(params string[] tags)
        {
            foreach (var tag in tags)
            {
                _git.LsRemoteLines.Add($"1111111111111111111111111111111111111111\trefs/tags/{tag}");
            }
        }

        #endregion

        #region Root layout

        [Fact]
        public async Task SyncModules_RootLayout_RegistersOneModuleWithVariablesAndOutputs()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildRootLayoutTree();

            await _sut.SyncModulesAsync(Source(ModuleSourceLayout.Root), false, CancellationToken.None);

            var module = await _db.Modules.Include(x => x.Versions).SingleAsync();

            Assert.Equal("catalog", module.Path);
            var version = Assert.Single(module.Versions);
            Assert.Equal("v0.1.0", version.Name);
            Assert.Equal(8, version.Variables.Count);
            Assert.Equal(4, version.Outputs.Count);
        }

        [Fact]
        public async Task SyncModules_RootLayout_StoresACleanCloneUrlWithNoSubdirectory()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildRootLayoutTree();

            await _sut.SyncModulesAsync(Source(ModuleSourceLayout.Root), false, CancellationToken.None);

            var version = await _db.ModuleVersions.SingleAsync();

            Assert.Equal("file:///srv/git/catalog.git", version.UrlLink);
            // No trace of the Gitlab "ref=master" url shape the old code patched.
            Assert.DoesNotContain("ref=", version.UrlLink);
        }

        #endregion

        #region Subdirectories layout

        [Fact]
        public async Task SyncModules_Subdirectories_RegistersOneModulePerModuleRoot()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildMonorepoTree();

            await _sut.SyncModulesAsync(Source(ModuleSourceLayout.Subdirectories), false, CancellationToken.None);

            var paths = await _db.Modules.Select(x => x.Path).OrderBy(x => x).ToListAsync();

            Assert.Equal(["catalog/machine-blank", "catalog/network-segment"], paths);
        }

        [Fact]
        public async Task SyncModules_Subdirectories_IgnoresHclMirrorsAndNonModuleDirectories()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildMonorepoTree();

            await _sut.SyncModulesAsync(Source(ModuleSourceLayout.Subdirectories), false, CancellationToken.None);

            var paths = await _db.Modules.Select(x => x.Path).ToListAsync();

            // hcl/ holds variables.tf, not variables.tf.json, so it is not a
            // module root. tools/ and .git/ hold neither.
            Assert.DoesNotContain(paths, x => x.Contains("hcl"));
            Assert.DoesNotContain(paths, x => x.Contains("tools"));
            Assert.DoesNotContain(paths, x => x.Contains(".git"));
            Assert.Equal(2, paths.Count);
        }

        [Fact]
        public async Task SyncModules_Subdirectories_UsesTerraformSubdirectorySyntaxInTheStoredUrl()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildMonorepoTree();

            await _sut.SyncModulesAsync(Source(ModuleSourceLayout.Subdirectories), false, CancellationToken.None);

            var module = await _db.Modules
                .Include(x => x.Versions)
                .SingleAsync(x => x.Path == "catalog/network-segment");

            Assert.Equal(
                "file:///srv/git/catalog.git//network-segment",
                module.Versions.Single().UrlLink);
        }

        [Fact]
        public async Task SyncModules_Subdirectories_EachModuleGetsItsOwnVariablesAndOutputs()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildMonorepoTree();

            await _sut.SyncModulesAsync(Source(ModuleSourceLayout.Subdirectories), false, CancellationToken.None);

            var segment = await _db.Modules.Include(x => x.Versions)
                .SingleAsync(x => x.Path == "catalog/network-segment");
            var machine = await _db.Modules.Include(x => x.Versions)
                .SingleAsync(x => x.Path == "catalog/machine-blank");

            Assert.Equal(8, segment.Versions.Single().Variables.Count);
            Assert.Equal(4, segment.Versions.Single().Outputs.Count);
            Assert.Equal(14, machine.Versions.Single().Variables.Count);
            Assert.Equal(5, machine.Versions.Single().Outputs.Count);
        }

        [Fact]
        public async Task SyncModules_Subdirectories_OneCloneServesEveryModule()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildMonorepoTree();

            await _sut.SyncModulesAsync(Source(ModuleSourceLayout.Subdirectories), false, CancellationToken.None);

            // Clones are per tag, not per module. That is the reason to prefer
            // a subdirectory-enumerating source over six separate sources.
            Assert.Equal(1, _git.CloneCallCount);
            Assert.Equal(2, await _db.Modules.CountAsync());
        }

        #endregion

        #region Variable fidelity

        [Fact]
        public async Task SyncModules_RequiredVariables_StayRequired()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildRootLayoutTree();

            await _sut.SyncModulesAsync(Source(ModuleSourceLayout.Root), false, CancellationToken.None);

            var variables = (await _db.ModuleVersions.SingleAsync()).Variables;

            // The catalog omits the default key for required variables, which
            // is the only way to avoid the "default": null trap.
            var required = variables.Where(x => !x.IsOptional).Select(x => x.Name).OrderBy(x => x).ToList();

            Assert.Equal(["datacenter", "distributed_switch", "name"], required);
        }

        [Fact]
        public async Task SyncModules_OptionalVariables_KeepTheirDefaults()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildRootLayoutTree();

            await _sut.SyncModulesAsync(Source(ModuleSourceLayout.Root), false, CancellationToken.None);

            var variables = (await _db.ModuleVersions.SingleAsync()).Variables;

            var vlan = variables.Single(x => x.Name == "vlan_id");
            Assert.True(vlan.IsOptional);
            Assert.Equal("0", vlan.DefaultValue);
            Assert.Equal("number", vlan.VariableType);

            var promiscuous = variables.Single(x => x.Name == "allow_promiscuous");
            Assert.True(promiscuous.IsOptional);
            Assert.Equal("bool", promiscuous.VariableType);

            // Pre-existing bug, asserted so it is visible rather than silent:
            // NumberToStringConverter falls through to JsonElement.ToString()
            // for a JSON boolean, which yields .NET casing. Terraform only
            // accepts lowercase true/false, so a designer who accepts this
            // default unedited generates HCL terraform rejects. The converter is
            // shared with the Gitlab path, so correcting it is a change in
            // behaviour for existing deployments and is left to its own change.
            Assert.Equal("False", promiscuous.DefaultValue);
        }

        [Fact]
        public async Task SyncModules_DescriptionsSurvive()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildRootLayoutTree();

            await _sut.SyncModulesAsync(Source(ModuleSourceLayout.Root), false, CancellationToken.None);

            var version = await _db.ModuleVersions.SingleAsync();

            Assert.All(version.Variables, x => Assert.False(string.IsNullOrWhiteSpace(x.Description)));
            Assert.All(version.Outputs, x => Assert.False(string.IsNullOrWhiteSpace(x.Description)));
        }

        #endregion

        #region Generated snippet

        [Fact]
        public async Task SyncModules_ProducesASnippetTerraformWouldAccept()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildMonorepoTree();

            await _sut.SyncModulesAsync(Source(ModuleSourceLayout.Subdirectories), false, CancellationToken.None);

            var module = await _db.Modules
                .Include(x => x.Versions)
                .SingleAsync(x => x.Path == "catalog/network-segment");

            var snippet = module.Versions.Single().ToSnippet(
                "red_lan",
                [
                    new Caster.Api.Domain.Models.ModuleValue { Name = "name", Value = "red-lan" },
                    new Caster.Api.Domain.Models.ModuleValue { Name = "vlan_id", Value = "0" },
                ]);

            Assert.Contains(
                "source = \"git::file:///srv/git/catalog.git//network-segment?ref=v0.1.0\"",
                snippet);
            Assert.Contains("name = \"red-lan\"", snippet);
            // A number must not be quoted or terraform rejects the type.
            Assert.Contains("vlan_id = 0", snippet);
        }

        #endregion

        #region caster.json display metadata

        [Fact]
        public async Task SyncModules_UsesCasterJsonDisplayNameAndDescriptionWhenPresent()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildMonorepoTree();

            await _sut.SyncModulesAsync(Source(ModuleSourceLayout.Subdirectories), false, CancellationToken.None);

            var module = await _db.Modules.SingleAsync(x => x.Path == "catalog/network-segment");

            Assert.Equal("Network Segment", module.Name);
            Assert.StartsWith("Create an isolated layer 2 segment", module.Description);
        }

        [Fact]
        public async Task SyncModules_FallsBackToTheSourceDescriptionWhenCasterJsonIsAbsent()
        {
            GivenTags("v0.1.0");

            var tree = Path.Combine(_scratch, "no-metadata");
            Directory.CreateDirectory(tree);
            File.Copy(
                Path.Combine(CatalogData, "network-segment", "variables.tf.json"),
                Path.Combine(tree, "variables.tf.json"));
            _git.SourceTree = tree;

            await _sut.SyncModulesAsync(Source(ModuleSourceLayout.Root), false, CancellationToken.None);

            var module = await _db.Modules.SingleAsync();

            Assert.Equal("catalog", module.Name);
            Assert.Equal("source level description", module.Description);
        }

        [Fact]
        public async Task SyncModules_MalformedCasterJsonDoesNotStopTheModule()
        {
            GivenTags("v0.1.0");

            var tree = Path.Combine(_scratch, "bad-metadata");
            Directory.CreateDirectory(tree);
            CopyModule("network-segment", tree);
            File.WriteAllText(Path.Combine(tree, "caster.json"), "{ this is not json");
            _git.SourceTree = tree;

            await _sut.SyncModulesAsync(Source(ModuleSourceLayout.Root), false, CancellationToken.None);

            var module = await _db.Modules.Include(x => x.Versions).SingleAsync();

            Assert.Equal("catalog", module.Name);
            Assert.Equal(8, module.Versions.Single().Variables.Count);
        }

        #endregion

        #region Versions from tags

        [Fact]
        public async Task SyncModules_MultipleTags_BecomeMultipleVersions()
        {
            GivenTags("v0.1.0", "v0.2.0");
            _git.SourceTree = BuildRootLayoutTree();

            await _sut.SyncModulesAsync(Source(ModuleSourceLayout.Root), false, CancellationToken.None);

            var names = await _db.ModuleVersions.Select(x => x.Name).OrderBy(x => x).ToListAsync();

            Assert.Equal(["v0.1.0", "v0.2.0"], names);
            // One clone per tag, no release API anywhere.
            Assert.Equal(2, _git.CloneCallCount);
        }

        [Fact]
        public async Task SyncModules_AnnotatedTags_AreNotCountedTwice()
        {
            _git.LsRemoteLines.Add("1111111111111111111111111111111111111111\trefs/tags/v0.1.0");
            _git.LsRemoteLines.Add("2222222222222222222222222222222222222222\trefs/tags/v0.1.0^{}");
            _git.SourceTree = BuildRootLayoutTree();

            await _sut.SyncModulesAsync(Source(ModuleSourceLayout.Root), false, CancellationToken.None);

            Assert.Equal(1, await _db.ModuleVersions.CountAsync());
            Assert.Equal(1, _git.CloneCallCount);
        }

        [Fact]
        public async Task SyncModules_NoTags_ThrowsRatherThanRegisteringAnUnusableModule()
        {
            _git.SourceTree = BuildRootLayoutTree();

            var exception = await Assert.ThrowsAsync<ModuleSyncException>(() =>
                _sut.SyncModulesAsync(Source(ModuleSourceLayout.Root), false, CancellationToken.None));

            Assert.Contains("no git tags", exception.Message);
            Assert.Equal(0, _git.CloneCallCount);
            Assert.Equal(0, await _db.Modules.CountAsync());
        }

        [Fact]
        public async Task SyncModules_NoModuleRootAnywhere_Throws()
        {
            GivenTags("v0.1.0");

            var tree = Path.Combine(_scratch, "empty");
            Directory.CreateDirectory(tree);
            File.WriteAllText(Path.Combine(tree, "README.md"), "nothing here\n");
            _git.SourceTree = tree;

            var exception = await Assert.ThrowsAsync<ModuleSyncException>(() =>
                _sut.SyncModulesAsync(Source(ModuleSourceLayout.Root), false, CancellationToken.None));

            Assert.Contains("variables.tf.json", exception.Message);
            Assert.Equal(0, await _db.Modules.CountAsync());
        }

        #endregion

        #region Re-sync behaviour

        [Fact]
        public async Task SyncModules_SecondSyncWithUnchangedTags_SkipsTheClone()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildMonorepoTree();
            var source = Source(ModuleSourceLayout.Subdirectories);

            await _sut.SyncModulesAsync(source, false, CancellationToken.None);
            Assert.Equal(1, _git.CloneCallCount);

            await _sut.SyncModulesAsync(source, false, CancellationToken.None);

            // Tags are immutable by convention, so an unchanged tag set means
            // there is nothing to re-read. ls-remote still runs; clone does not.
            Assert.Equal(1, _git.CloneCallCount);
            Assert.Equal(2, _git.LsRemoteCallCount);
            Assert.Equal(2, await _db.Modules.CountAsync());
        }

        [Fact]
        public async Task SyncModules_ForceUpdate_ClonesEvenWhenTagsAreUnchanged()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildRootLayoutTree();
            var source = Source(ModuleSourceLayout.Root);

            await _sut.SyncModulesAsync(source, false, CancellationToken.None);
            await _sut.SyncModulesAsync(source, forceUpdate: true, CancellationToken.None);

            Assert.Equal(2, _git.CloneCallCount);
        }

        [Fact]
        public async Task SyncModules_NewTag_IsPickedUpWithoutForceUpdate()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildRootLayoutTree();
            var source = Source(ModuleSourceLayout.Root);

            await _sut.SyncModulesAsync(source, false, CancellationToken.None);

            GivenTags("v0.2.0");
            await _sut.SyncModulesAsync(source, false, CancellationToken.None);

            var names = await _db.ModuleVersions.Select(x => x.Name).OrderBy(x => x).ToListAsync();

            Assert.Equal(["v0.1.0", "v0.2.0"], names);
        }

        [Fact]
        public async Task SyncModules_RemovedTag_IsDroppedFromVersions()
        {
            GivenTags("v0.1.0", "v0.2.0");
            _git.SourceTree = BuildRootLayoutTree();
            var source = Source(ModuleSourceLayout.Root);

            await _sut.SyncModulesAsync(source, false, CancellationToken.None);
            Assert.Equal(2, await _db.ModuleVersions.CountAsync());

            _git.LsRemoteLines.Clear();
            GivenTags("v0.2.0");
            await _sut.SyncModulesAsync(source, false, CancellationToken.None);

            var names = await _db.ModuleVersions.Select(x => x.Name).ToListAsync();
            Assert.Equal(["v0.2.0"], names);
        }

        [Fact]
        public async Task SyncModules_Resync_DoesNotDuplicateModules()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildMonorepoTree();
            var source = Source(ModuleSourceLayout.Subdirectories);

            await _sut.SyncModulesAsync(source, false, CancellationToken.None);
            await _sut.SyncModulesAsync(source, forceUpdate: true, CancellationToken.None);
            await _sut.SyncModulesAsync(source, forceUpdate: true, CancellationToken.None);

            Assert.Equal(2, await _db.Modules.CountAsync());
            Assert.Equal(2, await _db.ModuleVersions.CountAsync());
        }

        #endregion

        #region Single module sync

        [Fact]
        public async Task SyncModule_WithASubdirectory_SyncsOnlyThatModule()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildMonorepoTree();

            await _sut.SyncModuleAsync(
                Source(ModuleSourceLayout.Subdirectories),
                "network-segment",
                CancellationToken.None);

            var module = await _db.Modules.SingleAsync();
            Assert.Equal("catalog/network-segment", module.Path);
        }

        [Fact]
        public async Task SyncModule_WithNoSubdirectory_SyncsTheWholeSource()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildMonorepoTree();

            await _sut.SyncModuleAsync(Source(ModuleSourceLayout.Subdirectories), null, CancellationToken.None);

            Assert.Equal(2, await _db.Modules.CountAsync());
        }

        [Fact]
        public async Task SyncModule_WithAnUnknownSubdirectory_Throws()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildMonorepoTree();

            await Assert.ThrowsAsync<ModuleSyncException>(() => _sut.SyncModuleAsync(
                Source(ModuleSourceLayout.Subdirectories),
                "does-not-exist",
                CancellationToken.None));
        }

        #endregion

        #region Temp directory hygiene

        [Fact]
        public async Task SyncModules_DeletesEveryTempDirectoryOnSuccess()
        {
            GivenTags("v0.1.0", "v0.2.0");
            _git.SourceTree = BuildMonorepoTree();

            await _sut.SyncModulesAsync(Source(ModuleSourceLayout.Subdirectories), false, CancellationToken.None);

            Assert.Equal(2, _git.CloneDestinations.Count);
            Assert.All(_git.CloneDestinations, x => Assert.False(Directory.Exists(x)));
        }

        [Fact]
        public async Task SyncModules_DeletesTheTempDirectoryWhenTheCloneFails()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildRootLayoutTree();
            _git.CloneFailure = new GitCommandException("fatal: could not read Username");

            await Assert.ThrowsAsync<GitCommandException>(() =>
                _sut.SyncModulesAsync(Source(ModuleSourceLayout.Root), false, CancellationToken.None));

            var destination = Assert.Single(_git.CloneDestinations);
            Assert.False(Directory.Exists(destination));
        }

        [Fact]
        public async Task SyncModules_DeletesTheTempDirectoryWhenAModuleFileIsUnparseable()
        {
            GivenTags("v0.1.0");

            var tree = Path.Combine(_scratch, "bad-variables");
            Directory.CreateDirectory(tree);
            // Terraform's array form is valid HCL-JSON but the loader cannot
            // deserialize it, so this is the realistic parse failure.
            File.WriteAllText(
                Path.Combine(tree, "variables.tf.json"),
                "{\"variable\": {\"name\": [{\"type\": \"string\"}]}}");
            _git.SourceTree = tree;

            await Assert.ThrowsAsync<ModuleSyncException>(() =>
                _sut.SyncModulesAsync(Source(ModuleSourceLayout.Root), false, CancellationToken.None));

            var destination = Assert.Single(_git.CloneDestinations);
            Assert.False(Directory.Exists(destination));
        }

        [Fact]
        public async Task SyncModules_TempDirectoriesLiveUnderTheSystemTempPath()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildRootLayoutTree();

            await _sut.SyncModulesAsync(Source(ModuleSourceLayout.Root), false, CancellationToken.None);

            var destination = Assert.Single(_git.CloneDestinations);

            Assert.StartsWith(
                Path.Combine(Path.GetTempPath(), GitModuleRepositoryProvider.TempDirectoryName),
                destination);
        }

        #endregion
    }
}
