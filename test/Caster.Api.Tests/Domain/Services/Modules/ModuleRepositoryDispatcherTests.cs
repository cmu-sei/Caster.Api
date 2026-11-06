// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Services.Modules;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Options;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Caster.Api.Tests.Domain.Services.Modules
{
    [Trait("Category", "Unit")]
    [Trait("Category", "ModuleSources")]
    public class ModuleRepositoryDispatcherTests
    {
        private readonly RecordingProvider _git = new(ModuleSourceProviders.Git);
        private readonly RecordingProvider _gitlab = new(ModuleSourceProviders.Gitlab);
        private readonly ModuleSourceStatusStore _statuses = new();

        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private ModuleRepositoryDispatcher Build(TerraformOptions options)
        {
            var monitor = Substitute.For<IOptionsMonitor<TerraformOptions>>();
            monitor.CurrentValue.Returns(options);

            return new ModuleRepositoryDispatcher(
                [_git, _gitlab],
                monitor,
                _statuses,
                new RecordingLogger<ModuleRepositoryDispatcher>());
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
                sut.GetModulesAsync(false, Ct));

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
                sut.GetModulesAsync(false, Ct));
        }

        [Fact]
        public async Task GetModules_GroupIdWithoutApiUrl_IsNotAConfiguredSource()
        {
            // The "gitlab" http client sets BaseAddress from GitlabApiUrl and
            // throws on an empty string, so a group id alone is unusable.
            var sut = Build(new TerraformOptions { GitlabGroupId = 6 });

            await Assert.ThrowsAsync<ModuleSourceNotConfiguredException>(() =>
                sut.GetModulesAsync(false, Ct));
        }

        [Fact]
        public async Task GetModules_HalfFilledModuleSourceEntry_IsIgnoredNotFatal()
        {
            var sut = Build(new TerraformOptions
            {
                ModuleSources = [new ModuleSourceOptions { Name = "catalog" }],
            });

            await Assert.ThrowsAsync<ModuleSourceNotConfiguredException>(() =>
                sut.GetModulesAsync(false, Ct));
        }

        #endregion

        #region Routing

        [Fact]
        public async Task GetModules_GitSourceOnly_DoesNotTouchGitlab()
        {
            var sut = Build(new TerraformOptions { ModuleSources = [GitSource()] });

            await sut.GetModulesAsync(false, Ct);

            Assert.Equal(1, _git.SyncModulesCount);
            Assert.Equal(0, _gitlab.SyncModulesCount);
        }

        [Fact]
        public async Task GetModules_LegacyGitlabOnly_StillWorksWithNoNewConfiguration()
        {
            var sut = Build(GitlabConfigured());

            await sut.GetModulesAsync(false, Ct);

            Assert.Equal(1, _gitlab.SyncModulesCount);
            Assert.Equal(0, _git.SyncModulesCount);
        }

        [Fact]
        public async Task GetModules_BothKindsConfigured_SyncsBoth()
        {
            var options = GitlabConfigured();
            options.ModuleSources = [GitSource()];
            var sut = Build(options);

            await sut.GetModulesAsync(false, Ct);

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

            await sut.GetModulesAsync(false, Ct);

            Assert.Equal(3, _git.SyncModulesCount);
        }

        [Fact]
        public async Task GetModules_ForceUpdate_IsPassedThrough()
        {
            var sut = Build(new TerraformOptions { ModuleSources = [GitSource()] });

            await sut.GetModulesAsync(true, Ct);

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
                sut.GetModulesAsync(false, Ct));

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
                sut.GetModulesAsync(false, Ct));

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
                sut.GetModulesAsync(false, Ct));

            Assert.Equal(2, exception.Failures.Count);
        }

        [Fact]
        public async Task GetModules_RecordsEachSourcesOutcome()
        {
            _gitlab.Failure = new Exception("gitlab is down");
            var options = GitlabConfigured();
            options.ModuleSources = [GitSource()];
            var sut = Build(options);

            await Assert.ThrowsAsync<ModuleSyncException>(() => sut.GetModulesAsync(false, Ct));

            var statuses = sut.GetSourceStatuses().ToDictionary(x => x.Source.Name, x => x.Status);
            Assert.Equal(ModuleSourceState.Ok, statuses["catalog"].State);
            Assert.NotNull(statuses["catalog"].LastSuccess);
            Assert.Equal(ModuleSourceState.Error, statuses[ModuleRepositoryDispatcher.GitlabSourceName].State);
            Assert.Equal("gitlab is down", statuses[ModuleRepositoryDispatcher.GitlabSourceName].Message);
            Assert.Null(statuses[ModuleRepositoryDispatcher.GitlabSourceName].LastSuccess);
        }

        [Fact]
        public async Task GetModules_AFailureKeepsTheLastSuccess()
        {
            var sut = Build(new TerraformOptions { ModuleSources = [GitSource()] });
            await sut.GetModulesAsync(false, Ct);
            var synced = sut.GetSourceStatuses().Single().Status.LastSuccess;

            _git.Failure = new GitCommandException("fatal: repository not found");
            await Assert.ThrowsAsync<ModuleSyncException>(() => sut.GetModulesAsync(false, Ct));

            var status = sut.GetSourceStatuses().Single().Status;
            Assert.Equal(synced, status.LastSuccess);
            Assert.Equal(ModuleSourceState.Error, status.State);
            Assert.Equal("fatal: repository not found", status.Message);
        }

        [Fact]
        public async Task GetModules_NormalSync_RecordsOk()
        {
            var sut = Build(new TerraformOptions { ModuleSources = [GitSource()] });

            await sut.GetModulesAsync(false, Ct);

            var status = sut.GetSourceStatuses().Single().Status;
            Assert.Equal(ModuleSourceState.Ok, status.State);
            Assert.Null(status.Message);
        }

        [Theory]
        [InlineData(ModuleSourceState.Empty, "No Modules yet.")]
        [InlineData(ModuleSourceState.Warning, "Has Modules but no version tags, so none can be used. Tag the repository, e.g. v1.0.0.")]
        public async Task GetModules_EmptyOrWarningSource_IsASuccessfulReadWithNoError(ModuleSourceState state, string message)
        {
            _git.Result = new ModuleSyncResult(state, message);
            var sut = Build(new TerraformOptions { ModuleSources = [GitSource()] });

            // Does not throw, so the Module list is not failed by it.
            await sut.GetModulesAsync(false, Ct);

            var status = sut.GetSourceStatuses().Single().Status;
            Assert.Equal(state, status.State);
            Assert.Equal(message, status.Message);
            Assert.NotNull(status.LastSuccess);
            Assert.Equal(status.LastAttempt, status.LastSuccess);
        }

        [Fact]
        public async Task GetModules_SourceNotRead_KeepsItsLastStatus()
        {
            _git.Result = ModuleSyncResult.Empty("No Modules yet.");
            var sut = Build(new TerraformOptions { ModuleSources = [GitSource()] });
            await sut.GetModulesAsync(false, Ct);
            var before = sut.GetSourceStatuses().Single().Status;

            _git.Result = ModuleSyncResult.NotRead;
            await sut.GetModulesAsync(false, Ct);

            Assert.Same(before, sut.GetSourceStatuses().Single().Status);
        }

        [Fact]
        public void NotRead_HasNoStateAndCannotBeRecorded()
        {
            Assert.Null(ModuleSyncResult.NotRead.State);
            Assert.False(ModuleSyncResult.NotRead.Read);
            Assert.Throws<ArgumentException>(() => _statuses.RecordSuccess("catalog", ModuleSyncResult.NotRead));
        }

        [Fact]
        public async Task GetModules_FirstSyncNotRead_LeavesTheSourceUnrecorded()
        {
            _git.Result = ModuleSyncResult.NotRead;
            var sut = Build(new TerraformOptions { ModuleSources = [GitSource()] });

            await sut.GetModulesAsync(false, Ct);

            Assert.Null(sut.GetSourceStatuses().Single().Status);
        }

        [Fact]
        public async Task GetModules_AFailureQuotingTheSourceUrl_IsRecordedWithoutIt()
        {
            var source = GitSource();
            _git.Failure = new InvalidOperationException($"could not open {source.Url} for reading");
            var sut = Build(new TerraformOptions { ModuleSources = [source] });

            var exception = await Assert.ThrowsAsync<ModuleSyncException>(() => sut.GetModulesAsync(false, Ct));

            var status = sut.GetSourceStatuses().Single().Status;
            Assert.Equal("could not open <source url> for reading", status.Message);
            Assert.DoesNotContain(source.Url, exception.Message);
        }

        [Fact]
        public async Task GetModules_Failure_RecordsErrorAndItsMessage()
        {
            _git.Failure = new GitCommandException("fatal: repository not found");
            var sut = Build(new TerraformOptions { ModuleSources = [GitSource()] });

            await Assert.ThrowsAsync<ModuleSyncException>(() => sut.GetModulesAsync(false, Ct));

            var status = sut.GetSourceStatuses().Single().Status;
            Assert.Equal(ModuleSourceState.Error, status.State);
            Assert.Equal("fatal: repository not found", status.Message);
            Assert.Null(status.LastSuccess);
        }

        [Fact]
        public async Task GetModule_NamedModuleFailure_StillThrows()
        {
            _git.Failure = new ModuleSyncException("Module source 'catalog' has no git tags");
            var sut = Build(new TerraformOptions { ModuleSources = [GitSource()] });

            await Assert.ThrowsAsync<ModuleSyncException>(() => sut.GetModuleAsync("catalog/network-segment", Ct));
        }

        [Fact]
        public void GetSourceStatuses_ListsASourceNotYetSynced()
        {
            var sut = Build(new TerraformOptions { ModuleSources = [GitSource()] });

            var (source, status) = Assert.Single(sut.GetSourceStatuses());

            Assert.Equal("catalog", source.Name);
            Assert.Null(status);
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
                sut.GetModulesAsync(false, Ct));

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

            await sut.GetModulesAsync(false, Ct);

            Assert.Equal(1, _git.SyncModulesCount);
        }

        #endregion

        #region GetModuleAsync addressing

        [Fact]
        public async Task GetModule_SourceName_RoutesToThatSourceWithNoSubPath()
        {
            var sut = Build(new TerraformOptions { ModuleSources = [GitSource()] });

            await sut.GetModuleAsync("catalog", Ct);

            Assert.Equal([null], _git.ProviderIds);
        }

        [Fact]
        public async Task GetModule_SourceNameAndSubdirectory_PassesTheSubdirectoryThrough()
        {
            var sut = Build(new TerraformOptions { ModuleSources = [GitSource()] });

            await sut.GetModuleAsync("catalog/network-segment", Ct);

            Assert.Equal(["network-segment"], _git.ProviderIds);
        }

        [Fact]
        public async Task GetModule_GitlabProjectId_StillReachesGitlabUnchanged()
        {
            var sut = Build(GitlabConfigured());

            await sut.GetModuleAsync("11", Ct);

            Assert.Equal(["11"], _gitlab.ProviderIds);
            Assert.Equal(0, _git.SyncModuleCount);
        }

        [Fact]
        public async Task GetModule_GitlabProjectId_StillReachesGitlabWhenGitSourcesAlsoExist()
        {
            var options = GitlabConfigured();
            options.ModuleSources = [GitSource()];
            var sut = Build(options);

            await sut.GetModuleAsync("11", Ct);

            Assert.Equal(["11"], _gitlab.ProviderIds);
            Assert.Equal(0, _git.SyncModuleCount);
        }

        [Fact]
        public async Task GetModule_UnknownIdWithNoGitlabFallback_IsNotFound()
        {
            var sut = Build(new TerraformOptions { ModuleSources = [GitSource()] });

            var exception = await Assert.ThrowsAsync<EntityNotFoundException<Caster.Api.Domain.Models.Module>>(() =>
                sut.GetModuleAsync("11", Ct));

            Assert.Contains("catalog", exception.Message);
        }

        [Fact]
        public async Task GetModule_NothingConfigured_ReportsAnExplicitUnconfiguredCondition()
        {
            var sut = Build(new TerraformOptions());

            await Assert.ThrowsAsync<ModuleSourceNotConfiguredException>(() =>
                sut.GetModuleAsync("catalog", Ct));
        }

        [Fact]
        public void GetSourceStatuses_ASecondSourceNamedTheSameIgnoringCase_IsIgnored()
        {
            var sut = Build(new TerraformOptions { ModuleSources = [GitSource("catalog"), GitSource("Catalog")] });

            var (source, _) = Assert.Single(sut.GetSourceStatuses());

            Assert.Equal("catalog", source.Name);
        }

        [Fact]
        public async Task GetModule_SourceNameMatchingIsCaseInsensitive()
        {
            var sut = Build(new TerraformOptions { ModuleSources = [GitSource()] });

            await sut.GetModuleAsync("CATALOG/network-segment", Ct);

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

            /// <summary>What a sync that does not fail returns.</summary>
            public ModuleSyncResult Result { get; set; } = ModuleSyncResult.Ok;

            public Task<ModuleSyncResult> SyncModulesAsync(ModuleSourceOptions source, bool forceUpdate, CancellationToken cancellationToken)
            {
                SyncModulesCount++;
                ForceUpdates.Add(forceUpdate);

                return Failure == null ? Task.FromResult(Result) : Task.FromException<ModuleSyncResult>(Failure);
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
