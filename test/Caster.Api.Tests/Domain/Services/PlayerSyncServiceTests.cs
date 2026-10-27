// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Player.Vm.Api;
using Caster.Api.Domain.Services;
using Caster.Api.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Caster.Api.Tests.Domain.Services;

public class PlayerSyncServiceTests(DatabaseFixture fixture)
{
    private const string ChildFlag = "CASTER_PLAYER_SYNC_CHILD";
    private const string Completed = "player sync child assertions completed";
    private const string DatabaseFlag = "CASTER_PLAYER_SYNC_DATABASE";
    /// <summary>Allows child discovery and scheduling on a constrained runner, without delaying completion.</summary>
    private static readonly TimeSpan ChildDeadline = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task StartAsync_returns_success_without_logging_when_parallelism_is_zero()
    {
        await RunIsolatedStartup(AssertStartup, nameof(StartAsync_returns_success_without_logging_when_parallelism_is_zero));
    }

    [Fact]
    public async Task StopAsync_after_startup_with_zero_parallelism_throws()
    {
        await RunIsolatedStartup(AssertFailedStartupStop, nameof(StopAsync_after_startup_with_zero_parallelism_throws));
    }

    [Fact]
    public async Task StopAsync_leaves_the_removal_worker_running()
    {
        await RunRemovalShutdown();
    }

    private async Task RunRemovalShutdown()
    {
        if (Environment.GetEnvironmentVariable(ChildFlag) == "1")
        {
            await RunIsolatedStartup(AssertRemovalAfterStop, nameof(StopAsync_leaves_the_removal_worker_running));
            return;
        }

        await using var session = await fixture.BeginSessionAsync();
        await RunIsolatedStartup(AssertRemovalAfterStop, nameof(StopAsync_leaves_the_removal_worker_running), session.ConnectionString);
    }

    private static async Task RunIsolatedStartup(Func<Task> assertion, string method, string connectionString = null)
    {
        if (Environment.GetEnvironmentVariable(ChildFlag) == "1")
        {
            await assertion();
            Console.WriteLine(Completed);
            Console.Out.Flush();
            Environment.Exit(0);
            return;
        }

        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(typeof(PlayerSyncServiceTests).Assembly.Location);
        start.ArgumentList.Add("-method");
        start.ArgumentList.Add($"{typeof(PlayerSyncServiceTests).FullName}.{method}");
        start.ArgumentList.Add("-noColor");
        start.Environment[ChildFlag] = "1";
        if (connectionString != null)
            start.Environment[DatabaseFlag] = connectionString;
        using var child = Process.Start(start);
        var output = new ConcurrentQueue<string>();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stdout = CaptureOutput(child.StandardOutput, output, completion);
        var stderr = CaptureOutput(child.StandardError, output, completion);
        try
        {
            await ReadCompletion(completion.Task, output);
        }
        finally
        {
            if (!child.HasExited)
                child.Kill(entireProcessTree: true);
            await WaitForExitDeliberatelyWithoutCancellation(child);
            await Task.WhenAll(stdout, stderr);
        }
    }

    private static async Task ReadCompletion(Task completion, ConcurrentQueue<string> output)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(ChildDeadline);
        try
        {
            await completion.WaitAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException($"Player sync child did not report completion. Output:{Environment.NewLine}{string.Join(Environment.NewLine, output)}");
        }
    }

    private static async Task CaptureOutput(StreamReader reader, ConcurrentQueue<string> output, TaskCompletionSource completion)
    {
        string line;
        var failed = false;
        while ((line = await ReadLineDeliberatelyWithoutCancellation(reader)) != null)
        {
            output.Enqueue(line);
            if (line == Completed)
                completion.TrySetResult();
            else if (line.Contains("[FAIL]", StringComparison.Ordinal))
                failed = true;
            else if (failed && line.Contains("TEST EXECUTION SUMMARY", StringComparison.Ordinal))
                completion.TrySetException(new InvalidOperationException($"Player sync child failed. Output:{Environment.NewLine}{string.Join(Environment.NewLine, output)}"));
        }
    }

    private static Task WaitForExitDeliberatelyWithoutCancellation(Process child) => child.WaitForExitAsync();

    private static Task<string> ReadLineDeliberatelyWithoutCancellation(StreamReader reader) => reader.ReadLineAsync();

    private static Task AssertStartup()
    {
        var service = CreateService(out var logger);

        var startup = service.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(startup.IsCompletedSuccessfully);
        Assert.Null(typeof(PlayerSyncService).GetField("_jobQueue", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(service));
        Assert.Empty(logger.Entries);
        return Task.CompletedTask;
    }

    private static Task AssertFailedStartupStop()
    {
        var service = CreateService(out _);
        Assert.True(service.StartAsync(TestContext.Current.CancellationToken).IsCompletedSuccessfully);

        Assert.Throws<NullReferenceException>(() => { _ = service.StopAsync(TestContext.Current.CancellationToken); });
        return Task.CompletedTask;
    }

    private static async Task AssertRemovalAfterStop()
    {
        var firstRemoval = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextRemoval = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseRemoval = new SemaphoreSlim(0);
        var callerThread = Environment.CurrentManagedThreadId;
        var startupScopePending = 1;
        var removalCalls = 0;
        var services = new ServiceCollection();
        services.AddScoped(_ => new CasterContext(new DbContextOptionsBuilder<CasterContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable(DatabaseFlag)).Options));
        services.AddSingleton(Substitute.For<IPlayerVmApiClient>());
        using var provider = services.BuildServiceProvider();
        var scopes = Substitute.For<IServiceScopeFactory>();
        scopes.CreateScope().Returns(_ =>
        {
            // Only the synchronous startup lookup is exempt; removal can resume on its thread later.
            if (Environment.CurrentManagedThreadId == callerThread && Interlocked.Exchange(ref startupScopePending, 0) == 1)
                return provider.CreateScope();
            if (Interlocked.Increment(ref removalCalls) == 1)
                firstRemoval.TrySetResult();
            else
                nextRemoval.TrySetResult();
            releaseRemoval.Wait();
            return provider.CreateScope();
        });
        var options = Substitute.For<IOptionsMonitor<PlayerOptions>>();
        options.CurrentValue.Returns(new PlayerOptions { MaxParallelism = 1, RemoveLoopSeconds = 3600 });
        var service = new PlayerSyncService(scopes, options, new RecordingLogger<PlayerSyncService>());
        await service.StartAsync(TestContext.Current.CancellationToken);
        await firstRemoval.Task.WaitAsync(TestContext.Current.CancellationToken);

        await service.StopAsync(TestContext.Current.CancellationToken);
        releaseRemoval.Release();
        service.CheckRemovedResources();

        await nextRemoval.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, Volatile.Read(ref removalCalls));
    }

    private static PlayerSyncService CreateService(out RecordingLogger<PlayerSyncService> logger)
    {
        var scopes = Substitute.For<IServiceScopeFactory>();
        var blockRemoval = new ManualResetEventSlim();
        scopes.CreateScope().Returns(_ =>
        {
            blockRemoval.Wait();
            throw new InvalidOperationException("Removal worker released");
        });
        var options = Substitute.For<IOptionsMonitor<PlayerOptions>>();
        options.CurrentValue.Returns(new PlayerOptions { MaxParallelism = 0 });
        logger = new RecordingLogger<PlayerSyncService>();
        return new PlayerSyncService(scopes, options, logger);
    }
}
