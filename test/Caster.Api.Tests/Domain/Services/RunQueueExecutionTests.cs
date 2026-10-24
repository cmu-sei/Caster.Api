// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Data.Common;
using System.Threading;
using System.Reflection;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Events;
using Caster.Api.Domain.Services;
using Caster.Api.Features.Runs.EventHandlers;
using Caster.Api.Hubs;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Tests.Support;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Caster.Api.Tests.Domain.Services;

public class RunQueueExecutionTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task ExecuteAsync_drops_a_failed_run_query_without_logging_its_exception()
    {
        await Db.Database.ExecuteSqlRawAsync("ALTER TABLE runs RENAME TO unavailable_runs", Ct);
        var handlerLog = new RecordingLogger<RunAddedHandler>();
        var failure = new QueryFailure();
        var queueLog = new RecordingLogger<RunQueueService>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssemblyContaining<RunAddedHandler>());
        services.AddScoped(_ => CasterContextFactory.CreateContext(
            options => options.UseNpgsql(Session.ConnectionString).AddInterceptors(failure), Db.ServiceProvider));
        services.AddSingleton(new DbContextOptionsBuilder<CasterContext>().UseNpgsql(Session.ConnectionString).Options);
        services.AddSingleton<ILogger<RunAddedHandler>>(handlerLog);
        services.AddSingleton(Substitute.For<ITerraformService>());
        services.AddSingleton<ILockService>(new LockService());
        services.AddSingleton<IOutputService>(new OutputService());
        services.AddSingleton(new TerraformOptions());
        using var provider = services.BuildServiceProvider();
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queue = new RunQueueService(new ScopeCompletionProvider(provider, disposed), queueLog,
            Options.Create(new TerraformOptions { MaxConcurrentRuns = 1 }), new HubRecorder<ProjectHub>());
        queue.Add(new RunAdded { RunId = Guid.NewGuid(), WorkspaceId = Guid.NewGuid() });

        var execution = InvokeExecuteAsync(queue);
        try
        {
            await disposed.Task.WaitAsync(Ct);
        }
        finally
        {
            await queue.StopAsync(Ct);
            await execution.WaitAsync(Ct);
            await ReleasedSlot(queue).WaitAsync(Ct);
        }

        Assert.Empty(queue.GetQueuePositions());
        Assert.Empty(queueLog.At(LogLevel.Error));
        Assert.Empty(handlerLog.At(LogLevel.Error));
        var exception = Assert.IsType<PostgresException>(failure.Exception);
        Assert.Equal(PostgresErrorCodes.UndefinedTable, exception.SqlState);
        Assert.Contains("runs", exception.MessageText);
    }

    private static Task InvokeExecuteAsync(RunQueueService queue) =>
        (Task)typeof(RunQueueService).GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(queue, null);

    private static SemaphoreSlim ReleasedSlot(RunQueueService queue) =>
        (SemaphoreSlim)typeof(RunQueueService).GetField("_concurrencyLimiter", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(queue);

    private sealed class QueryFailure : DbCommandInterceptor
    {
        public Exception Exception { get; private set; }

        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            Exception = eventData.Exception;
            return Task.CompletedTask;
        }
    }

    private sealed class ScopeCompletionProvider(IServiceProvider provider, TaskCompletionSource disposed)
        : IServiceProvider, IServiceScopeFactory
    {
        public object GetService(Type type) => type == typeof(IServiceScopeFactory) ? this : provider.GetService(type);

        public IServiceScope CreateScope() => new CompletionScope(provider.CreateScope(), disposed);
    }

    private sealed class CompletionScope(IServiceScope scope, TaskCompletionSource disposed) : IServiceScope
    {
        public IServiceProvider ServiceProvider => scope.ServiceProvider;

        public void Dispose()
        {
            scope.Dispose();
            disposed.TrySetResult();
        }
    }
}
