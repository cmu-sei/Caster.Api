// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
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

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _ = ExecuteAsync();
            return System.Threading.Tasks.Task.CompletedTask;
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
            await Task.Run(() =>
            {
                foreach (var item in _queue.GetConsumingEnumerable())
                {
                    _ = Task.Run(async () =>
                    {
                        await this.Handle(item);
                    });
                }
            });
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
