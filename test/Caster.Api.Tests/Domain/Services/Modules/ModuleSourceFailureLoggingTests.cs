// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Services.Modules;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Options;
using Crucible.Api.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Caster.Api.Tests.Domain.Services.Modules
{
    /// <summary>
    /// Every Module list refreshes every module source, so a source that stays broken fails the same way each
    /// time. These pin what reaches the log when it does: once in full, again on a change or a backoff, and a
    /// line when it recovers.
    /// </summary>
    [Trait("Category", "Unit")]
    [Trait("Category", "ModuleSources")]
    public class ModuleSourceFailureLoggingTests
    {
        private readonly FakeProvider _git = new();
        private readonly ManualTimeProvider _clock = new();
        private readonly RecordingLogger<ModuleRepositoryDispatcher> _logger = new();

        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private ModuleRepositoryDispatcher Build(params string[] sourceNames)
        {
            var monitor = Substitute.For<IOptionsMonitor<TerraformOptions>>();
            monitor.CurrentValue.Returns(new TerraformOptions
            {
                ModuleSources = [.. sourceNames.Select(x => new ModuleSourceOptions
                {
                    Name = x,
                    Provider = ModuleSourceProviders.Git,
                    Url = $"file:///srv/git/{x}.git",
                })],
            });

            return new ModuleRepositoryDispatcher(
                [_git],
                monitor,
                new ModuleSourceStatusStore(),
                new ModuleSourceFailureLog(_clock),
                _logger);
        }

        private static async Task Refresh(ModuleRepositoryDispatcher sut)
        {
            try
            {
                await sut.GetModulesAsync(false, Ct);
            }
            catch (ModuleSyncException)
            {
            }
        }

        private IReadOnlyList<RecordingLogger.LogEntry> Errors => _logger.At(LogLevel.Error);

        [Fact]
        public async Task FirstFailure_IsLoggedInFullAtError()
        {
            var failure = new InvalidOperationException("git clone failed");
            _git.Failures["catalog"] = failure;

            await Refresh(Build("catalog"));

            var entry = Assert.Single(Errors);
            Assert.Same(failure, entry.Exception);
            Assert.Contains("catalog", entry.Message);
        }

        [Fact]
        public async Task IdenticalRepeat_IsNotLoggedAgainAboveDebug()
        {
            _git.Failures["catalog"] = new InvalidOperationException("git clone failed");
            var sut = Build("catalog");

            await Refresh(sut);
            for (var i = 0; i < 5; i++)
            {
                _clock.Advance(TimeSpan.FromSeconds(30));
                await Refresh(sut);
            }

            Assert.Single(Errors);
            Assert.DoesNotContain(_logger.Entries, x => x.Level == LogLevel.Warning);
            Assert.Equal(5, _logger.At(LogLevel.Debug).Count);
        }

        [Fact]
        public async Task ChangedError_IsLoggedAgain()
        {
            var sut = Build("catalog");
            _git.Failures["catalog"] = new InvalidOperationException("could not resolve host");
            await Refresh(sut);

            _clock.Advance(TimeSpan.FromSeconds(30));
            _git.Failures["catalog"] = new InvalidOperationException("authentication failed");
            await Refresh(sut);

            Assert.Equal(2, Errors.Count);
            Assert.Equal("authentication failed", Errors[1].Exception.Message);
        }

        [Fact]
        public async Task StillFailingAfterTheBackoff_RemindsOnceAndTheBackoffGrows()
        {
            _git.Failures["catalog"] = new InvalidOperationException("git clone failed");
            var sut = Build("catalog");
            await Refresh(sut);

            _clock.Advance(ModuleSourceFailureLog.FirstReminderInterval - TimeSpan.FromSeconds(1));
            await Refresh(sut);
            Assert.Single(Errors);

            _clock.Advance(TimeSpan.FromSeconds(1));
            await Refresh(sut);
            Assert.Equal(2, Errors.Count);
            Assert.Contains("still failing", Errors[1].Message);
            Assert.Contains("1 repeats", Errors[1].Message);

            // The next reminder waits twice as long.
            _clock.Advance(ModuleSourceFailureLog.FirstReminderInterval);
            await Refresh(sut);
            Assert.Equal(2, Errors.Count);

            _clock.Advance(ModuleSourceFailureLog.FirstReminderInterval);
            await Refresh(sut);
            Assert.Equal(3, Errors.Count);
        }

        [Fact]
        public void TheReminderBackoffStopsGrowingAtTheCap()
        {
            var failureLog = new ModuleSourceFailureLog(_clock);
            failureLog.RecordFailure("catalog", "boom", out _);

            // Each reminder waits twice as long as the one before: 15m, 30m, 1h, 2h, 4h, then 4h again.
            foreach (var minutes in new[] { 15, 30, 60, 120, 240, 240, 240 })
            {
                var wait = TimeSpan.FromMinutes(minutes);

                _clock.Advance(wait - TimeSpan.FromSeconds(1));
                Assert.Equal(ModuleSourceFailureLogAction.Suppress, failureLog.RecordFailure("catalog", "boom", out _));

                _clock.Advance(TimeSpan.FromSeconds(1));
                Assert.Equal(ModuleSourceFailureLogAction.Remind, failureLog.RecordFailure("catalog", "boom", out _));
            }
        }

        [Fact]
        public void ChangedError_StartsTheBackoffOver()
        {
            var failureLog = new ModuleSourceFailureLog(_clock);
            failureLog.RecordFailure("catalog", "one", out _);
            _clock.Advance(ModuleSourceFailureLog.FirstReminderInterval);
            failureLog.RecordFailure("catalog", "one", out _);

            Assert.Equal(ModuleSourceFailureLogAction.Log, failureLog.RecordFailure("catalog", "two", out _));

            _clock.Advance(ModuleSourceFailureLog.FirstReminderInterval);
            Assert.Equal(ModuleSourceFailureLogAction.Remind, failureLog.RecordFailure("catalog", "two", out _));
        }

        [Fact]
        public async Task SourceNamedLikeAnInternalKey_IsNotConfusedWithAnIgnoredEntry()
        {
            _git.Failures["ignored:half"] = new InvalidOperationException("git clone failed");
            var monitor = Substitute.For<IOptionsMonitor<TerraformOptions>>();
            monitor.CurrentValue.Returns(new TerraformOptions
            {
                ModuleSources =
                [
                    new ModuleSourceOptions { Name = "half", Provider = ModuleSourceProviders.Git },
                    new ModuleSourceOptions { Name = "ignored:half", Provider = ModuleSourceProviders.Git, Url = "file:///x" },
                ],
            });
            var sut = new ModuleRepositoryDispatcher(
                [_git], monitor, new ModuleSourceStatusStore(), new ModuleSourceFailureLog(_clock), _logger);

            await Refresh(sut);
            await Refresh(sut);

            Assert.Single(Errors);
            Assert.Single(_logger.At(LogLevel.Warning));
        }

        [Fact]
        public async Task Recovery_IsLoggedOnceAndTheNextFailureIsLoggedInFullAgain()
        {
            _git.Failures["catalog"] = new InvalidOperationException("git clone failed");
            var sut = Build("catalog");
            await Refresh(sut);
            _clock.Advance(TimeSpan.FromMinutes(2));
            await Refresh(sut);

            _git.Failures.Clear();
            _clock.Advance(TimeSpan.FromMinutes(1));
            await Refresh(sut);
            await Refresh(sut);

            var recovered = Assert.Single(_logger.At(LogLevel.Information));
            Assert.Contains("catalog", recovered.Message);
            Assert.Contains("synced again", recovered.Message);

            _git.Failures["catalog"] = new InvalidOperationException("git clone failed");
            await Refresh(sut);
            Assert.Equal(2, Errors.Count);
        }

        [Fact]
        public async Task TwoSources_AreTrackedIndependently()
        {
            _git.Failures["one"] = new InvalidOperationException("same error");
            _git.Failures["two"] = new InvalidOperationException("same error");
            var sut = Build("one", "two");

            await Refresh(sut);
            Assert.Equal(2, Errors.Count);

            // "one" recovers while "two" keeps failing: only "one" gets a recovery line, and "two" stays quiet.
            _git.Failures.Remove("one");
            _clock.Advance(TimeSpan.FromSeconds(30));
            await Refresh(sut);

            Assert.Equal(2, Errors.Count);
            var recovered = Assert.Single(_logger.At(LogLevel.Information));
            Assert.Contains("one", recovered.Message);
        }

        [Fact]
        public async Task UnregisteredProvider_IsLoggedOnceNotOnEveryRefresh()
        {
            var monitor = Substitute.For<IOptionsMonitor<TerraformOptions>>();
            monitor.CurrentValue.Returns(new TerraformOptions
            {
                ModuleSources = [new ModuleSourceOptions { Name = "odd", Provider = "nope", Url = "file:///x" }],
            });
            var sut = new ModuleRepositoryDispatcher(
                [_git], monitor, new ModuleSourceStatusStore(), new ModuleSourceFailureLog(_clock), _logger);

            await Refresh(sut);
            await Refresh(sut);
            await Refresh(sut);

            var entry = Assert.Single(Errors);
            Assert.Contains("not registered", entry.Message);
        }

        [Fact]
        public async Task NothingConfigured_IsLoggedOnceNotOnEveryRefresh()
        {
            var sut = Build();

            for (var i = 0; i < 3; i++)
            {
                await Assert.ThrowsAsync<ModuleSourceNotConfiguredException>(() => sut.GetModulesAsync(false, Ct));
            }

            Assert.Single(_logger.At(LogLevel.Warning));
        }

        [Fact]
        public async Task IgnoredEntry_IsLoggedOnceNotOnEveryRefresh()
        {
            var monitor = Substitute.For<IOptionsMonitor<TerraformOptions>>();
            monitor.CurrentValue.Returns(new TerraformOptions
            {
                ModuleSources =
                [
                    new ModuleSourceOptions { Name = "half", Provider = ModuleSourceProviders.Git },
                    new ModuleSourceOptions { Name = "catalog", Provider = ModuleSourceProviders.Git, Url = "file:///x" },
                ],
            });
            var sut = new ModuleRepositoryDispatcher(
                [_git], monitor, new ModuleSourceStatusStore(), new ModuleSourceFailureLog(_clock), _logger);

            await sut.GetModulesAsync(false, Ct);
            await sut.GetModulesAsync(false, Ct);
            sut.GetSourceStatuses();

            Assert.Single(_logger.At(LogLevel.Warning));
        }

        [Fact]
        public async Task TheFailureLog_NeverAddsMoreThanTheErrorMessage()
        {
            // The dispatcher logs what the provider's exception says, which the git provider has already redacted;
            // nothing here adds the source's Url or options.
            _git.Failures["catalog"] = new InvalidOperationException("git clone failed");

            await Refresh(Build("catalog"));

            Assert.DoesNotContain(_logger.Entries, x => x.Message.Contains("file:///srv/git"));
        }

        private class FakeProvider : IModuleRepositoryProvider
        {
            public string Provider => ModuleSourceProviders.Git;

            public Dictionary<string, Exception> Failures { get; } = new(StringComparer.OrdinalIgnoreCase);

            public Task<bool> SyncModulesAsync(ModuleSourceOptions source, bool forceUpdate, CancellationToken cancellationToken) =>
                Failures.TryGetValue(source.Name, out var failure)
                    ? Task.FromException<bool>(failure)
                    : Task.FromResult(true);

            public Task<bool> SyncModuleAsync(ModuleSourceOptions source, string providerId, CancellationToken cancellationToken) =>
                Task.FromResult(true);
        }

        private class ManualTimeProvider : TimeProvider
        {
            private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

            public override DateTimeOffset GetUtcNow() => _now;

            public void Advance(TimeSpan by) => _now += by;
        }
    }
}
