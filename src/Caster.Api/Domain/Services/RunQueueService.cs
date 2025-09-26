// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Events;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Utilities.Synchronization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Caster.Api.Domain.Services
{
    public interface IRunQueueService : IHostedService
    {
        void Add(INotification notification);
    }

    public class RunQueueService : IRunQueueService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<RunQueueService> _logger;

        private readonly BlockingCollection<INotification> _queue = new BlockingCollection<INotification>();

        public RunQueueService(IServiceProvider serviceProvider, ILogger<RunQueueService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
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
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public void Add(INotification notification)
        {
            _queue.Add(notification);
        }

        private async Task ExecuteAsync()
        {
            foreach (var item in _queue.GetConsumingEnumerable())
            {
                _ = this.Handle(item);
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
                    if (run.Status == RunStatus.Applying)
                    {
                        _ = Task.Run(async () =>
                        {
                            await this.Handle(new ApplyAdded { ApplyId = run.Apply.Id });
                        });
                    }
                    else if (run.Status == RunStatus.Planning)
                    {
                        _ = Task.Run(async () =>
                        {
                            await this.Handle(new RunAdded { RunId = run.Id });
                        });
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
