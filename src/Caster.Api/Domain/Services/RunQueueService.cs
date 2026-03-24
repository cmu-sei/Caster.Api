// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Events;
using Caster.Api.Domain.Models;
using Caster.Api.Hubs;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Utilities.Synchronization;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Caster.Api.Domain.Services
{
    public record QueuePosition(Guid ItemId, Guid WorkspaceId, int Position, int Total);

    public interface IRunQueueService : IHostedService
    {
        void Add(INotification notification);
        bool Cancel(Guid runId);
        IReadOnlyList<QueuePosition> GetQueuePositions();
        QueuePosition GetQueuePosition(Guid runId);
    }

    public class RunQueueService : IRunQueueService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<RunQueueService> _logger;
        private readonly IHubContext<ProjectHub> _projectHub;

        private readonly ConcurrentQueue<ApplyAdded> _applyQueue = new();
        private readonly ConcurrentQueue<RunAdded> _planQueue = new();
        private readonly ConcurrentDictionary<Guid, bool> _cancelledRunIds = new();
        private readonly SemaphoreSlim _itemAvailable = new(0);
        private readonly SemaphoreSlim _concurrencyLimiter;

        public RunQueueService(
            IServiceProvider serviceProvider,
            ILogger<RunQueueService> logger,
            IOptions<TerraformOptions> terraformOptions,
            IHubContext<ProjectHub> projectHub)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _projectHub = projectHub;
            var options = terraformOptions.Value;
            _concurrencyLimiter = new SemaphoreSlim(
                options.MaxConcurrentRuns > 0
                    ? options.MaxConcurrentRuns
                    : int.MaxValue);
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("RunQueueService starting...");
            var lockResults = await StartRecoverWorkspaces();
            _ = RecoverWorkspaces(lockResults);
            _ = Task.Run(() => ExecuteAsync());
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public void Add(INotification notification)
        {
            if (notification is ApplyAdded apply)
                _applyQueue.Enqueue(apply);
            else if (notification is RunAdded run)
                _planQueue.Enqueue(run);
            _itemAvailable.Release();
        }

        public bool Cancel(Guid runId)
        {
            return _cancelledRunIds.TryAdd(runId, true);
        }

        public IReadOnlyList<QueuePosition> GetQueuePositions()
        {
            var applies = _applyQueue.ToArray().Where(a => !_cancelledRunIds.ContainsKey(a.RunId)).ToArray();
            var plans = _planQueue.ToArray().Where(p => !_cancelledRunIds.ContainsKey(p.RunId)).ToArray();
            var total = applies.Length + plans.Length;
            var positions = new List<QueuePosition>(total);
            int pos = 1;
            foreach (var a in applies)
                positions.Add(new QueuePosition(a.RunId, a.WorkspaceId, pos++, total));
            foreach (var p in plans)
                positions.Add(new QueuePosition(p.RunId, p.WorkspaceId, pos++, total));
            return positions;
        }

        public QueuePosition GetQueuePosition(Guid runId)
        {
            return GetQueuePositions().FirstOrDefault(p => p.ItemId == runId);
        }

        private async Task ExecuteAsync()
        {
            while (true)
            {
                await _itemAvailable.WaitAsync();
                await BroadcastQueuePositions();
                await _concurrencyLimiter.WaitAsync();
                var item = DequeueNext();
                if (item == null) { _concurrencyLimiter.Release(); continue; }
                _ = HandleWithRelease(item);
                await BroadcastQueuePositions();
            }
        }

        private INotification DequeueNext()
        {
            while (_applyQueue.TryDequeue(out var apply))
            {
                if (_cancelledRunIds.TryRemove(apply.RunId, out _))
                    continue;
                return apply;
            }

            while (_planQueue.TryDequeue(out var plan))
            {
                if (_cancelledRunIds.TryRemove(plan.RunId, out _))
                    continue;
                return plan;
            }

            _logger.LogWarning("DequeueNext called but both queues empty");
            return null;
        }

        private async Task HandleWithRelease(INotification item)
        {
            try
            {
                await Handle(item);
            }
            finally
            {
                _concurrencyLimiter.Release();
            }
        }

        private async Task BroadcastQueuePositions()
        {
            try
            {
                var positions = GetQueuePositions();
                foreach (var pos in positions)
                {
                    await _projectHub.Clients
                        .Group(pos.WorkspaceId.ToString())
                        .SendAsync("RunQueuePositionUpdated", pos);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting queue positions");
            }
        }

        private async Task<Dictionary<Guid, AsyncLock.AsyncLockResult>> StartRecoverWorkspaces()
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var terraformService = scope.ServiceProvider.GetRequiredService<ITerraformService>();
                var lockService = scope.ServiceProvider.GetRequiredService<ILockService>();
                var db = scope.ServiceProvider.GetRequiredService<CasterContext>();
                var activeWorkspaceIds = await terraformService.GetActiveWorkspaces();

                var inProgressRuns = await db.Runs
                    .Where(x => RunHelpers.GetActiveStatuses().Contains(x.Status))
                    .Include(x => x.Apply)
                    .Include(x => x.Plan)
                    .ToListAsync();

                var inProgressWorkspaceIds = inProgressRuns.Select(x => x.WorkspaceId).ToHashSet();
                activeWorkspaceIds = activeWorkspaceIds.Where(x => !inProgressWorkspaceIds.Contains(x));

                foreach (var run in inProgressRuns)
                {
                    if (run.Status == RunStatus.Applying || run.Status == RunStatus.ApplyQueued)
                    {
                        Add(new ApplyAdded { ApplyId = run.Apply.Id, RunId = run.Id, WorkspaceId = run.WorkspaceId });
                    }
                    else if (run.Status == RunStatus.Planning)
                    {
                        Add(new RunAdded { RunId = run.Id, WorkspaceId = run.WorkspaceId });
                    }
                }

                Dictionary<Guid, AsyncLock> locks = new();
                Dictionary<Guid, AsyncLock.AsyncLockResult> lockResults = new();

                foreach (var workspaceId in activeWorkspaceIds)
                {
                    locks.Add(workspaceId, lockService.GetWorkspaceLock(workspaceId));
                }

                foreach (var kvp in locks)
                {
                    var lockResult = await kvp.Value.LockAsync(0);

                    if (lockResult.AcquiredLock)
                    {
                        lockResults.Add(kvp.Key, lockResult);
                    }
                }

                return lockResults;
            }
        }

        private async Task RecoverWorkspaces(Dictionary<Guid, AsyncLock.AsyncLockResult> lockResults)
        {
            _logger.LogInformation("Continuing background recovery after startup...");

            var disposedLocks = new HashSet<Guid>();

            try
            {
                foreach (var kvp in lockResults)
                {
                    try
                    {
                        using (var scope = _serviceProvider.CreateScope())
                        {
                            var terraformService = scope.ServiceProvider.GetRequiredService<ITerraformService>();
                            var db = scope.ServiceProvider.GetRequiredService<CasterContext>();
                            var options = scope.ServiceProvider.GetRequiredService<TerraformOptions>();

                            var workspace = await db.Workspaces.Where(x => x.Id == kvp.Key).FirstOrDefaultAsync();

                            await terraformService.Resume(workspace);

                            var workingDir = workspace.GetPath(options.RootWorkingDirectory);
                            await workspace.RetrieveState(workingDir);
                            await db.SaveChangesAsync();
                            workspace.CleanupFileSystem(options.RootWorkingDirectory);

                            kvp.Value.Dispose();
                            disposedLocks.Add(kvp.Key);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error recovering workspace {WorkspaceId}", kvp.Key);
                    }
                }
            }
            finally
            {
                foreach (var kvp in lockResults)
                {
                    if (!disposedLocks.Contains(kvp.Key))
                    {
                        kvp.Value.Dispose();
                    }
                }

                _logger.LogInformation("Released all workspace locks.");
            }
        }

        private async Task Handle(INotification notification)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                await mediator.Publish(notification);
            }
        }
    }
}
