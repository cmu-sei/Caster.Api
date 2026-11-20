// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Caster.Api.Domain.Services;
using Caster.Api.Domain.Services.Modules;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Caster.Api.Tests.Domain.Services.Modules
{
    /// <summary>
    /// Exercises the plain-git provider against the real Crucible module
    /// catalog's variables.tf.json / outputs.tf.json / caster.json, copied into
    /// Data/catalog. Hand-written JSON is avoided on purpose: the loader's four
    /// quirks (object form only, no "default": null, no object(...) types, one
    /// block type per file) make invented fixtures misleading. What a sync
    /// stores is read back from the test's own PostgreSQL database.
    /// </summary>
    [Trait("Category", "Unit")]
    [Trait("Category", "ModuleSources")]
    // Both classes clone under the one shared temp root, and the end-to-end
    // hygiene tests count its children, so they must not run in parallel.
    [Collection(GitModuleTempRoot.Name)]
    public class GitModuleRepositoryProviderTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
    {
        private static readonly string CatalogData =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "catalog");

        private readonly FakeGitCommandRunner _git = new();
        private readonly string _scratch = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"caster-test-{Guid.NewGuid():N}")).FullName;
        private GitModuleRepositoryProvider _provider;

        /// <summary>The provider under test, over the test's database and <see cref="_git"/>.</summary>
        private GitModuleRepositoryProvider Sut => _provider ??= Provider(Db, _git);

        public override async ValueTask DisposeAsync()
        {
            if (Directory.Exists(_scratch))
            {
                Directory.Delete(_scratch, recursive: true);
            }

            await base.DisposeAsync();
        }

        #region Helpers

        private static GitModuleRepositoryProvider Provider(Caster.Api.Data.CasterContext db, IGitCommandRunner git, ILockService locks = null) =>
            new(db, git, locks ?? new LockService(), new RecordingLogger<GitModuleRepositoryProvider>());

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

            await Sut.SyncModulesAsync(Source(ModuleSourceLayout.Root), false, Ct);

            var module = await ReadBack(db => db.Modules.Include(x => x.Versions).SingleAsync(Ct));

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

            await Sut.SyncModulesAsync(Source(ModuleSourceLayout.Root), false, Ct);

            var version = await ReadBack(db => db.ModuleVersions.SingleAsync(Ct));

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

            await Sut.SyncModulesAsync(Source(ModuleSourceLayout.Subdirectories), false, Ct);

            var paths = await ReadBack(db => db.Modules.Select(x => x.Path).OrderBy(x => x).ToListAsync(Ct));

            Assert.Equal(["catalog/machine-blank", "catalog/network-segment"], paths);
        }

        [Fact]
        public async Task SyncModules_Subdirectories_IgnoresHclMirrorsAndNonModuleDirectories()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildMonorepoTree();

            await Sut.SyncModulesAsync(Source(ModuleSourceLayout.Subdirectories), false, Ct);

            var paths = await ReadBack(db => db.Modules.Select(x => x.Path).ToListAsync(Ct));

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

            await Sut.SyncModulesAsync(Source(ModuleSourceLayout.Subdirectories), false, Ct);

            var module = await ReadBack(db => db.Modules
                .Include(x => x.Versions)
                .SingleAsync(x => x.Path == "catalog/network-segment", Ct));

            Assert.Equal(
                "file:///srv/git/catalog.git//network-segment",
                module.Versions.Single().UrlLink);
        }

        [Fact]
        public async Task SyncModules_Subdirectories_EachModuleGetsItsOwnVariablesAndOutputs()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildMonorepoTree();

            await Sut.SyncModulesAsync(Source(ModuleSourceLayout.Subdirectories), false, Ct);

            var segment = await ReadBack(db => db.Modules.Include(x => x.Versions)
                .SingleAsync(x => x.Path == "catalog/network-segment", Ct));
            var machine = await ReadBack(db => db.Modules.Include(x => x.Versions)
                .SingleAsync(x => x.Path == "catalog/machine-blank", Ct));

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

            await Sut.SyncModulesAsync(Source(ModuleSourceLayout.Subdirectories), false, Ct);

            // Clones are per tag, not per module. That is the reason to prefer
            // a subdirectory-enumerating source over six separate sources.
            Assert.Equal(1, _git.CloneCallCount);
            Assert.Equal(2, await ReadBack(db => db.Modules.CountAsync(Ct)));
        }

        #endregion

        #region Variable fidelity

        [Fact]
        public async Task SyncModules_RequiredVariables_StayRequired()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildRootLayoutTree();

            await Sut.SyncModulesAsync(Source(ModuleSourceLayout.Root), false, Ct);

            var variables = (await ReadBack(db => db.ModuleVersions.SingleAsync(Ct))).Variables;

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

            await Sut.SyncModulesAsync(Source(ModuleSourceLayout.Root), false, Ct);

            var variables = (await ReadBack(db => db.ModuleVersions.SingleAsync(Ct))).Variables;

            var vlan = variables.Single(x => x.Name == "vlan_id");
            Assert.True(vlan.IsOptional);
            Assert.Equal("0", vlan.DefaultValue);
            Assert.Equal("number", vlan.VariableType);

            var promiscuous = variables.Single(x => x.Name == "allow_promiscuous");
            Assert.True(promiscuous.IsOptional);
            Assert.Equal("bool", promiscuous.VariableType);

            // Terraform only accepts lowercase true/false.
            Assert.Equal("false", promiscuous.DefaultValue);
        }

        [Fact]
        public async Task SyncModules_DescriptionsSurvive()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildRootLayoutTree();

            await Sut.SyncModulesAsync(Source(ModuleSourceLayout.Root), false, Ct);

            var version = await ReadBack(db => db.ModuleVersions.SingleAsync(Ct));

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

            await Sut.SyncModulesAsync(Source(ModuleSourceLayout.Subdirectories), false, Ct);

            var module = await ReadBack(db => db.Modules
                .Include(x => x.Versions)
                .SingleAsync(x => x.Path == "catalog/network-segment", Ct));

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

            await Sut.SyncModulesAsync(Source(ModuleSourceLayout.Subdirectories), false, Ct);

            var module = await ReadBack(db => db.Modules.SingleAsync(x => x.Path == "catalog/network-segment", Ct));

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

            await Sut.SyncModulesAsync(Source(ModuleSourceLayout.Root), false, Ct);

            var module = await ReadBack(db => db.Modules.SingleAsync(Ct));

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

            await Sut.SyncModulesAsync(Source(ModuleSourceLayout.Root), false, Ct);

            var module = await ReadBack(db => db.Modules.Include(x => x.Versions).SingleAsync(Ct));

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

            await Sut.SyncModulesAsync(Source(ModuleSourceLayout.Root), false, Ct);

            var names = await ReadBack(db => db.ModuleVersions.Select(x => x.Name).OrderBy(x => x).ToListAsync(Ct));

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

            await Sut.SyncModulesAsync(Source(ModuleSourceLayout.Root), false, Ct);

            Assert.Equal(1, await ReadBack(db => db.ModuleVersions.CountAsync(Ct)));
            Assert.Equal(1, _git.CloneCallCount);
        }

        [Fact]
        public async Task SyncModules_NoTags_ThrowsRatherThanRegisteringAnUnusableModule()
        {
            _git.SourceTree = BuildRootLayoutTree();

            var exception = await Assert.ThrowsAsync<ModuleSyncException>(() =>
                Sut.SyncModulesAsync(Source(ModuleSourceLayout.Root), false, Ct));

            Assert.Contains("no git tags", exception.Message);
            Assert.Equal(0, _git.CloneCallCount);
            Assert.Equal(0, await ReadBack(db => db.Modules.CountAsync(Ct)));
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
                Sut.SyncModulesAsync(Source(ModuleSourceLayout.Root), false, Ct));

            Assert.Contains("variables.tf.json", exception.Message);
            Assert.Equal(0, await ReadBack(db => db.Modules.CountAsync(Ct)));
        }

        #endregion

        #region Per-module tags

        [Fact]
        public async Task SyncModules_PerModuleTag_VersionsOnlyThatModule()
        {
            GivenTags("v0.1.0", "network-segment/v0.2.0");
            _git.SourceTree = BuildMonorepoTree();

            await Sut.SyncModulesAsync(Source(ModuleSourceLayout.Subdirectories), false, Ct);

            var segment = await ReadBack(db => db.Modules.Include(x => x.Versions)
                .SingleAsync(x => x.Path == "catalog/network-segment", Ct));
            var machine = await ReadBack(db => db.Modules.Include(x => x.Versions)
                .SingleAsync(x => x.Path == "catalog/machine-blank", Ct));

            Assert.Equal(
                ["network-segment/v0.2.0", "v0.1.0"],
                segment.Versions.Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal));
            Assert.Equal(["v0.1.0"], machine.Versions.Select(x => x.Name));
        }

        [Fact]
        public async Task SyncModules_PerModuleTag_SnippetReferencesTheFullTag()
        {
            GivenTags("network-segment/v1.2.0");
            _git.SourceTree = BuildMonorepoTree();

            await Sut.SyncModulesAsync(Source(ModuleSourceLayout.Subdirectories), false, Ct);

            var version = (await ReadBack(db => db.Modules.Include(x => x.Versions).SingleAsync(Ct))).Versions.Single();
            var snippet = version.ToSnippet("lan", [new Caster.Api.Domain.Models.ModuleValue { Name = "name", Value = "lan" }]);

            // The ref is the git tag itself, slash and all; go-getter passes it
            // to git unchanged.
            Assert.Contains(
                "source = \"git::file:///srv/git/catalog.git//network-segment?ref=network-segment/v1.2.0\"",
                snippet);
        }

        [Fact]
        public async Task SyncModules_PerModuleTagForAnotherModule_IsSkippedBySingleModuleSync()
        {
            GivenTags("v0.1.0", "machine-blank/v0.2.0");
            _git.SourceTree = BuildMonorepoTree();

            await Sut.SyncModuleAsync(
                Source(ModuleSourceLayout.Subdirectories),
                "network-segment",
                Ct);

            // machine-blank's tag cannot hold a network-segment version, so it
            // is not cloned at all.
            Assert.Equal(["v0.1.0"], _git.ClonedTags);
            Assert.Equal(["v0.1.0"], await ReadBack(db => db.ModuleVersions.Select(x => x.Name).ToListAsync(Ct)));
        }

        [Fact]
        public async Task SyncModules_RootLayout_TreatsASlashTagAsPlain()
        {
            GivenTags("release/v1.0.0");
            _git.SourceTree = BuildRootLayoutTree();

            await Sut.SyncModulesAsync(Source(ModuleSourceLayout.Root), false, Ct);

            Assert.Equal("release/v1.0.0", (await ReadBack(db => db.ModuleVersions.SingleAsync(Ct))).Name);
        }

        [Fact]
        public async Task SyncModules_SecondSyncWithUnchangedPerModuleTags_SkipsTheClone()
        {
            GivenTags("v0.1.0", "network-segment/v0.2.0");
            _git.SourceTree = BuildMonorepoTree();
            var source = Source(ModuleSourceLayout.Subdirectories);

            await Sut.SyncModulesAsync(source, false, Ct);
            Assert.Equal(2, _git.CloneCallCount);

            // machine-blank lacks network-segment's tag, which must not read
            // as out of date.
            await Sut.SyncModulesAsync(source, false, Ct);

            Assert.Equal(2, _git.CloneCallCount);
        }

        [Fact]
        public async Task SyncModules_NewPerModuleTag_IsPickedUpWithoutForceUpdate()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildMonorepoTree();
            var source = Source(ModuleSourceLayout.Subdirectories);

            await Sut.SyncModulesAsync(source, false, Ct);

            GivenTags("machine-blank/v0.2.0");
            await Sut.SyncModulesAsync(source, false, Ct);

            var machine = await ReadBack(db => db.Modules.Include(x => x.Versions)
                .SingleAsync(x => x.Path == "catalog/machine-blank", Ct));

            Assert.Equal(
                ["machine-blank/v0.2.0", "v0.1.0"],
                machine.Versions.Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal));
        }

        [Fact]
        public async Task SyncModules_ModuleReleasedOnlyThroughItsOwnTag_IsDiscoveredWithoutForceUpdate()
        {
            // v0.1.0 holds network-segment alone; machine-blank first ships
            // under its own tag. Every existing Module is current, so only the
            // unknown tag prefix can reveal the new one.
            var early = Path.Combine(_scratch, "early");
            Directory.CreateDirectory(Path.Combine(early, "network-segment"));
            CopyModule("network-segment", Path.Combine(early, "network-segment"));
            _git.SourceTreesByTag["v0.1.0"] = early;
            _git.SourceTree = BuildMonorepoTree();
            GivenTags("v0.1.0");
            var source = Source(ModuleSourceLayout.Subdirectories);

            await Sut.SyncModulesAsync(source, false, Ct);
            Assert.Equal(1, await ReadBack(db => db.Modules.CountAsync(Ct)));

            GivenTags("machine-blank/v1.0.0");
            await Sut.SyncModulesAsync(source, false, Ct);

            var machine = await ReadBack(db => db.Modules.Include(x => x.Versions)
                .SingleAsync(x => x.Path == "catalog/machine-blank", Ct));
            Assert.Equal(["machine-blank/v1.0.0"], machine.Versions.Select(x => x.Name));
        }

        [Fact]
        public async Task SyncModules_MetadataComesFromTheHighestVersionNotTheLastTagByName()
        {
            // Ordinal order would read "v0.1.0" after "network-segment/v2.0.0"
            // and let the older caster.json win.
            var old = BuildMonorepoTree();
            File.WriteAllText(
                Path.Combine(old, "network-segment", "caster.json"),
                "{\"displayName\": \"Old Segment\"}");
            _git.SourceTreesByTag["v0.1.0"] = old;

            var current = Path.Combine(_scratch, "current");
            Directory.CreateDirectory(Path.Combine(current, "network-segment"));
            CopyModule("network-segment", Path.Combine(current, "network-segment"));
            _git.SourceTreesByTag["network-segment/v2.0.0"] = current;

            GivenTags("v0.1.0", "network-segment/v2.0.0");

            await Sut.SyncModulesAsync(Source(ModuleSourceLayout.Subdirectories), false, Ct);

            var module = await ReadBack(db => db.Modules.SingleAsync(x => x.Path == "catalog/network-segment", Ct));
            Assert.Equal("Network Segment", module.Name);
            Assert.Equal(["v0.1.0", "network-segment/v2.0.0"], _git.ClonedTags);
        }

        [Theory]
        [InlineData(ModuleSourceLayout.Subdirectories, "network-segment/v1.2.0", "network-segment")]
        [InlineData(ModuleSourceLayout.Subdirectories, "v1.2.0", null)]
        [InlineData(ModuleSourceLayout.Subdirectories, "network-segment-v1.2.0", null)]
        [InlineData(ModuleSourceLayout.Subdirectories, "/v1.2.0", null)]
        [InlineData(ModuleSourceLayout.Subdirectories, "network-segment/", null)]
        [InlineData(ModuleSourceLayout.Root, "network-segment/v1.2.0", null)]
        public void GetTagModule_ReadsTheModuleFromASlashPrefix(ModuleSourceLayout layout, string tag, string expected)
        {
            Assert.Equal(expected, GitModuleRepositoryProvider.GetTagModule(Source(layout), tag));
        }

        #endregion

        #region Re-sync behaviour

        [Fact]
        public async Task SyncModules_SecondSyncWithUnchangedTags_SkipsTheClone()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildMonorepoTree();
            var source = Source(ModuleSourceLayout.Subdirectories);

            await Sut.SyncModulesAsync(source, false, Ct);
            Assert.Equal(1, _git.CloneCallCount);

            await Sut.SyncModulesAsync(source, false, Ct);

            // Tags are immutable by convention, so an unchanged tag set means
            // there is nothing to re-read. ls-remote still runs; clone does not.
            Assert.Equal(1, _git.CloneCallCount);
            Assert.Equal(2, _git.LsRemoteCallCount);
            Assert.Equal(2, await ReadBack(db => db.Modules.CountAsync(Ct)));
        }

        [Fact]
        public async Task SyncModules_RecordsTheRequiredProviders()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildRootLayoutTree();
            File.WriteAllText(Path.Combine(_git.SourceTree, "main.tf"),
                "terraform {\n  required_providers {\n    proxmox = { source = \"bpg/proxmox\" }\n  }\n}\n");

            await Sut.SyncModulesAsync(Source(ModuleSourceLayout.Root), false, Ct);

            var version = await ReadBack(db => db.ModuleVersions.SingleAsync(Ct));
            Assert.Equal(["bpg/proxmox"], version.Providers);
        }

        [Fact]
        public async Task SyncModules_VersionsSyncedBeforeProvidersWereRead_AreReadAgain()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildMonorepoTree();
            var source = Source(ModuleSourceLayout.Subdirectories);
            await Sut.SyncModulesAsync(source, false, Ct);

            // what a sync from before the providers column left behind
            await Db.ModuleVersions.ExecuteUpdateAsync(x => x.SetProperty(v => v.Providers, (string[])null), Ct);

            await Sut.SyncModulesAsync(source, false, Ct);

            Assert.Equal(2, _git.CloneCallCount);
            Assert.False(await ReadBack(db => db.ModuleVersions.AnyAsync(v => v.Providers == null, Ct)));

            // and once read, an unchanged tag set skips the clone again
            await Sut.SyncModulesAsync(source, false, Ct);
            Assert.Equal(2, _git.CloneCallCount);
        }

        [Fact]
        public async Task SyncModules_ModuleAddedInALaterTag_SecondSyncSkipsTheClone()
        {
            // v0.1.0 predates machine-blank, so it never carries that tag. That
            // must not read as out of date, or every request re-clones every tag.
            var early = Path.Combine(_scratch, "early");
            Directory.CreateDirectory(Path.Combine(early, "network-segment"));
            CopyModule("network-segment", Path.Combine(early, "network-segment"));
            _git.SourceTreesByTag["v0.1.0"] = early;
            _git.SourceTree = BuildMonorepoTree();
            GivenTags("v0.1.0", "v0.2.0");
            var source = Source(ModuleSourceLayout.Subdirectories);

            await Sut.SyncModulesAsync(source, false, Ct);
            Assert.Equal(2, _git.CloneCallCount);

            await Sut.SyncModulesAsync(source, false, Ct);

            Assert.Equal(2, _git.CloneCallCount);
        }

        [Fact]
        public async Task SyncModules_ConcurrentRequests_SyncTheSourceOnce()
        {
            // Two requests, each with its own context on the one database, as
            // when a page loads the module list twice at once.
            var locks = new LockService();
            var tree = BuildMonorepoTree();
            var first = Runner(tree);
            var second = Runner(tree);
            await using var firstContext = NewContext();
            await using var secondContext = NewContext();

            await Task.WhenAll(
                Provider(firstContext, first, locks).SyncModulesAsync(Source(ModuleSourceLayout.Subdirectories), false, Ct),
                Provider(secondContext, second, locks).SyncModulesAsync(Source(ModuleSourceLayout.Subdirectories), false, Ct));

            // The second waited, then found the first's result current.
            Assert.Equal(1, first.CloneCallCount + second.CloneCallCount);
            Assert.Equal(2, await ReadBack(db => db.ModuleVersions.CountAsync(Ct)));
        }

        /// <summary>A runner that answers one tag and clones <paramref name="tree"/> slowly enough to overlap another.</summary>
        private static FakeGitCommandRunner Runner(string tree)
        {
            var runner = new FakeGitCommandRunner { SourceTree = tree, CloneDelay = TimeSpan.FromMilliseconds(100) };
            runner.LsRemoteLines.Add("1111111111111111111111111111111111111111\trefs/tags/v0.1.0");

            return runner;
        }

        [Fact]
        public async Task SyncModules_ForceUpdate_ClonesEvenWhenTagsAreUnchanged()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildRootLayoutTree();
            var source = Source(ModuleSourceLayout.Root);

            await Sut.SyncModulesAsync(source, false, Ct);
            await Sut.SyncModulesAsync(source, forceUpdate: true, Ct);

            Assert.Equal(2, _git.CloneCallCount);
        }

        [Fact]
        public async Task SyncModules_NewTag_IsPickedUpWithoutForceUpdate()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildRootLayoutTree();
            var source = Source(ModuleSourceLayout.Root);

            await Sut.SyncModulesAsync(source, false, Ct);

            GivenTags("v0.2.0");
            await Sut.SyncModulesAsync(source, false, Ct);

            var names = await ReadBack(db => db.ModuleVersions.Select(x => x.Name).OrderBy(x => x).ToListAsync(Ct));

            Assert.Equal(["v0.1.0", "v0.2.0"], names);
        }

        [Fact]
        public async Task SyncModules_RemovedTag_IsDroppedFromVersions()
        {
            GivenTags("v0.1.0", "v0.2.0");
            _git.SourceTree = BuildRootLayoutTree();
            var source = Source(ModuleSourceLayout.Root);

            await Sut.SyncModulesAsync(source, false, Ct);
            Assert.Equal(2, await ReadBack(db => db.ModuleVersions.CountAsync(Ct)));

            _git.LsRemoteLines.Clear();
            GivenTags("v0.2.0");
            await Sut.SyncModulesAsync(source, false, Ct);

            var names = await ReadBack(db => db.ModuleVersions.Select(x => x.Name).ToListAsync(Ct));
            Assert.Equal(["v0.2.0"], names);
        }

        [Fact]
        public async Task SyncModules_Resync_DoesNotDuplicateModules()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildMonorepoTree();
            var source = Source(ModuleSourceLayout.Subdirectories);

            await Sut.SyncModulesAsync(source, false, Ct);
            await Sut.SyncModulesAsync(source, forceUpdate: true, Ct);
            await Sut.SyncModulesAsync(source, forceUpdate: true, Ct);

            Assert.Equal(2, await ReadBack(db => db.Modules.CountAsync(Ct)));
            Assert.Equal(2, await ReadBack(db => db.ModuleVersions.CountAsync(Ct)));
        }

        #endregion

        #region Single module sync

        [Fact]
        public async Task SyncModule_WithASubdirectory_SyncsOnlyThatModule()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildMonorepoTree();

            await Sut.SyncModuleAsync(
                Source(ModuleSourceLayout.Subdirectories),
                "network-segment",
                Ct);

            var module = await ReadBack(db => db.Modules.SingleAsync(Ct));
            Assert.Equal("catalog/network-segment", module.Path);
        }

        [Fact]
        public async Task SyncModule_WithNoSubdirectory_SyncsTheWholeSource()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildMonorepoTree();

            await Sut.SyncModuleAsync(Source(ModuleSourceLayout.Subdirectories), null, Ct);

            Assert.Equal(2, await ReadBack(db => db.Modules.CountAsync(Ct)));
        }

        [Fact]
        public async Task SyncModule_WithAnUnknownSubdirectory_Throws()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildMonorepoTree();

            await Assert.ThrowsAsync<ModuleSyncException>(() => Sut.SyncModuleAsync(
                Source(ModuleSourceLayout.Subdirectories),
                "does-not-exist",
                Ct));
        }

        #endregion

        #region Temp directory hygiene

        [Fact]
        public async Task SyncModules_DeletesEveryTempDirectoryOnSuccess()
        {
            GivenTags("v0.1.0", "v0.2.0");
            _git.SourceTree = BuildMonorepoTree();

            await Sut.SyncModulesAsync(Source(ModuleSourceLayout.Subdirectories), false, Ct);

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
                Sut.SyncModulesAsync(Source(ModuleSourceLayout.Root), false, Ct));

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
                Sut.SyncModulesAsync(Source(ModuleSourceLayout.Root), false, Ct));

            var destination = Assert.Single(_git.CloneDestinations);
            Assert.False(Directory.Exists(destination));
        }

        [Fact]
        public async Task SyncModules_TempDirectoriesLiveUnderTheSystemTempPath()
        {
            GivenTags("v0.1.0");
            _git.SourceTree = BuildRootLayoutTree();

            await Sut.SyncModulesAsync(Source(ModuleSourceLayout.Root), false, Ct);

            var destination = Assert.Single(_git.CloneDestinations);

            Assert.StartsWith(
                Path.Combine(Path.GetTempPath(), GitModuleRepositoryProvider.TempDirectoryName),
                destination);
        }

        #endregion
    }
}
