// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Services.Modules;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Caster.Api.Tests.Unit.Modules
{
    [Trait("Category", "Unit")]
    [Trait("Category", "ModuleSources")]
    public class ModuleRepositoryDispatcherTests
    {
        private readonly RecordingProvider _git = new(ModuleSourceProviders.Git);
        private readonly RecordingProvider _gitlab = new(ModuleSourceProviders.Gitlab);

        private ModuleRepositoryDispatcher Build(TerraformOptions options)
        {
            var monitor = Substitute.For<IOptionsMonitor<TerraformOptions>>();
            monitor.CurrentValue.Returns(options);

            return new ModuleRepositoryDispatcher(
                [_git, _gitlab],
                monitor,
                Substitute.For<ILogger<ModuleRepositoryDispatcher>>());
        }

        private static ModuleSourceOptions GitSource(string name = "catalog") => new()
        {
            Name = name,
            Provider = ModuleSourceProviders.Git,
            Url = $"file:///srv/git/{name}.git",
        };

        private static TerraformOptions GitlabConfigured() => new()
        {
            GitlabApiUrl = "https://gitlab.local/api/v4/",
            GitlabGroupId = 6,
        };

        #region Nothing configured

        [Fact]
        public async Task GetModules_NothingConfigured_ReportsAnExplicitUnconfiguredCondition()
        {
            var sut = Build(new TerraformOptions());

            var exception = await Assert.ThrowsAsync<ModuleSourceNotConfiguredException>(() =>
                sut.GetModulesAsync(false, CancellationToken.None));

            Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, exception.GetStatusCode());
            Assert.Contains("Terraform:ModuleSources", exception.Message);
            Assert.Equal(0, _git.SyncModulesCount);
            Assert.Equal(0, _gitlab.SyncModulesCount);
        }

        [Fact]
        public async Task GetModules_GitlabApiUrlWithoutGroupId_IsNotAConfiguredSource()
        {
            var sut = Build(new TerraformOptions { GitlabApiUrl = "https://gitlab.local/api/v4/" });

            await Assert.ThrowsAsync<ModuleSourceNotConfiguredException>(() =>
                sut.GetModulesAsync(false, CancellationToken.None));
        }

        [Fact]
        public async Task GetModules_GroupIdWithoutApiUrl_IsNotAConfiguredSource()
        {
            // The "gitlab" http client sets BaseAddress from GitlabApiUrl and
            // throws on an empty string, so a group id alone is unusable.
            var sut = Build(new TerraformOptions { GitlabGroupId = 6 });

            await Assert.ThrowsAsync<ModuleSourceNotConfiguredException>(() =>
                sut.GetModulesAsync(false, CancellationToken.None));
        }

        [Fact]
        public async Task GetModules_HalfFilledModuleSourceEntry_IsIgnoredNotFatal()
        {
            var sut = Build(new TerraformOptions
            {
                ModuleSources = [new ModuleSourceOptions { Name = "catalog" }],
            });

            await Assert.ThrowsAsync<ModuleSourceNotConfiguredException>(() =>
                sut.GetModulesAsync(false, CancellationToken.None));
        }

        #endregion

        #region Routing

        [Fact]
        public async Task GetModules_GitSourceOnly_DoesNotTouchGitlab()
        {
            var sut = Build(new TerraformOptions { ModuleSources = [GitSource()] });

            await sut.GetModulesAsync(false, CancellationToken.None);

            Assert.Equal(1, _git.SyncModulesCount);
            Assert.Equal(0, _gitlab.SyncModulesCount);
        }

        [Fact]
        public async Task GetModules_LegacyGitlabOnly_StillWorksWithNoNewConfiguration()
        {
            var sut = Build(GitlabConfigured());

            await sut.GetModulesAsync(false, CancellationToken.None);

            Assert.Equal(1, _gitlab.SyncModulesCount);
            Assert.Equal(0, _git.SyncModulesCount);
        }

        [Fact]
        public async Task GetModules_BothKindsConfigured_SyncsBoth()
        {
            var options = GitlabConfigured();
            options.ModuleSources = [GitSource()];
            var sut = Build(options);

            await sut.GetModulesAsync(false, CancellationToken.None);

            Assert.Equal(1, _gitlab.SyncModulesCount);
            Assert.Equal(1, _git.SyncModulesCount);
        }

        [Fact]
        public async Task GetModules_SeveralGitSources_EachIsSynced()
        {
            var sut = Build(new TerraformOptions
            {
                ModuleSources = [GitSource("one"), GitSource("two"), GitSource("three")],
            });

            await sut.GetModulesAsync(false, CancellationToken.None);

            Assert.Equal(3, _git.SyncModulesCount);
        }

        [Fact]
        public async Task GetModules_ForceUpdate_IsPassedThrough()
        {
            var sut = Build(new TerraformOptions { ModuleSources = [GitSource()] });

            await sut.GetModulesAsync(true, CancellationToken.None);

            Assert.Equal([true], _git.ForceUpdates);
        }

        #endregion

        #region Failure reporting

        [Fact]
        public async Task GetModules_SourceFails_SurfacesItInsteadOfSwallowingIt()
        {
            _git.Failure = new GitCommandException("fatal: repository not found");
            var sut = Build(new TerraformOptions { ModuleSources = [GitSource()] });

            var exception = await Assert.ThrowsAsync<ModuleSyncException>(() =>
                sut.GetModulesAsync(false, CancellationToken.None));

            Assert.Equal(System.Net.HttpStatusCode.BadGateway, exception.GetStatusCode());
            Assert.Contains("catalog", exception.Message);
            Assert.Contains("repository not found", exception.Message);
        }

        [Fact]
        public async Task GetModules_OneSourceFails_TheOthersAreStillAttempted()
        {
            _gitlab.Failure = new Exception("gitlab is down");
            var options = GitlabConfigured();
            options.ModuleSources = [GitSource()];
            var sut = Build(options);

            var exception = await Assert.ThrowsAsync<ModuleSyncException>(() =>
                sut.GetModulesAsync(false, CancellationToken.None));

            // The healthy source was still synced and persisted; the caller is
            // told only about the one that failed.
            Assert.Equal(1, _git.SyncModulesCount);
            Assert.Single(exception.Failures);
            Assert.Contains("gitlab is down", exception.Message);
        }

        [Fact]
        public async Task GetModules_EverySourceFails_ReportsAllOfThem()
        {
            _git.Failure = new Exception("git boom");
            _gitlab.Failure = new Exception("gitlab boom");
            var options = GitlabConfigured();
            options.ModuleSources = [GitSource()];
            var sut = Build(options);

            var exception = await Assert.ThrowsAsync<ModuleSyncException>(() =>
                sut.GetModulesAsync(false, CancellationToken.None));

            Assert.Equal(2, exception.Failures.Count);
        }

        [Fact]
        public async Task GetModules_UnregisteredProvider_IsReportedNotIgnored()
        {
            var sut = Build(new TerraformOptions
            {
                ModuleSources =
                [
                    new ModuleSourceOptions
                    {
                        Name = "registry",
                        Provider = "TerraformRegistry",
                        Url = "https://registry.terraform.io",
                    },
                ],
            });

            var exception = await Assert.ThrowsAsync<ModuleSyncException>(() =>
                sut.GetModulesAsync(false, CancellationToken.None));

            Assert.Contains("TerraformRegistry", exception.Message);
            Assert.Contains("not registered", exception.Message);
        }

        [Fact]
        public async Task GetModules_Cancellation_PropagatesRatherThanBecomingASyncFailure()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            _git.Failure = new OperationCanceledException();
            var sut = Build(new TerraformOptions { ModuleSources = [GitSource()] });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                sut.GetModulesAsync(false, cts.Token));
        }

        [Fact]
        public async Task GetModules_ProviderNameMatchingIsCaseInsensitive()
        {
            var sut = Build(new TerraformOptions
            {
                ModuleSources = [new ModuleSourceOptions { Name = "catalog", Provider = "git", Url = "file:///x.git" }],
            });

            await sut.GetModulesAsync(false, CancellationToken.None);

            Assert.Equal(1, _git.SyncModulesCount);
        }

        #endregion

        #region GetModuleAsync addressing

        [Fact]
        public async Task GetModule_SourceName_RoutesToThatSourceWithNoSubPath()
        {
            var sut = Build(new TerraformOptions { ModuleSources = [GitSource()] });

            await sut.GetModuleAsync("catalog", CancellationToken.None);

            Assert.Equal([null], _git.ProviderIds);
        }

        [Fact]
        public async Task GetModule_SourceNameAndSubdirectory_PassesTheSubdirectoryThrough()
        {
            var sut = Build(new TerraformOptions { ModuleSources = [GitSource()] });

            await sut.GetModuleAsync("catalog/network-segment", CancellationToken.None);

            Assert.Equal(["network-segment"], _git.ProviderIds);
        }

        [Fact]
        public async Task GetModule_GitlabProjectId_StillReachesGitlabUnchanged()
        {
            var sut = Build(GitlabConfigured());

            await sut.GetModuleAsync("11", CancellationToken.None);

            Assert.Equal(["11"], _gitlab.ProviderIds);
            Assert.Equal(0, _git.SyncModuleCount);
        }

        [Fact]
        public async Task GetModule_GitlabProjectId_StillReachesGitlabWhenGitSourcesAlsoExist()
        {
            var options = GitlabConfigured();
            options.ModuleSources = [GitSource()];
            var sut = Build(options);

            await sut.GetModuleAsync("11", CancellationToken.None);

            Assert.Equal(["11"], _gitlab.ProviderIds);
            Assert.Equal(0, _git.SyncModuleCount);
        }

        [Fact]
        public async Task GetModule_UnknownIdWithNoGitlabFallback_IsNotFound()
        {
            var sut = Build(new TerraformOptions { ModuleSources = [GitSource()] });

            var exception = await Assert.ThrowsAsync<EntityNotFoundException<Caster.Api.Domain.Models.Module>>(() =>
                sut.GetModuleAsync("11", CancellationToken.None));

            Assert.Contains("catalog", exception.Message);
        }

        [Fact]
        public async Task GetModule_NothingConfigured_ReportsAnExplicitUnconfiguredCondition()
        {
            var sut = Build(new TerraformOptions());

            await Assert.ThrowsAsync<ModuleSourceNotConfiguredException>(() =>
                sut.GetModuleAsync("catalog", CancellationToken.None));
        }

        [Fact]
        public async Task GetModule_SourceNameMatchingIsCaseInsensitive()
        {
            var sut = Build(new TerraformOptions { ModuleSources = [GitSource()] });

            await sut.GetModuleAsync("CATALOG/network-segment", CancellationToken.None);

            Assert.Equal(["network-segment"], _git.ProviderIds);
        }

        #endregion

        private class RecordingProvider(string provider) : IModuleRepositoryProvider
        {
            public string Provider { get; } = provider;

            public Exception Failure { get; set; }

            public int SyncModulesCount { get; private set; }
            public int SyncModuleCount { get; private set; }
            public List<bool> ForceUpdates { get; } = [];
            public List<string> ProviderIds { get; } = [];

            public Task<bool> SyncModulesAsync(ModuleSourceOptions source, bool forceUpdate, CancellationToken cancellationToken)
            {
                SyncModulesCount++;
                ForceUpdates.Add(forceUpdate);

                return Failure == null ? Task.FromResult(true) : Task.FromException<bool>(Failure);
            }

            public Task<bool> SyncModuleAsync(ModuleSourceOptions source, string providerId, CancellationToken cancellationToken)
            {
                SyncModuleCount++;
                ProviderIds.Add(providerId);

                return Failure == null ? Task.FromResult(true) : Task.FromException<bool>(Failure);
            }
        }
    }
}
