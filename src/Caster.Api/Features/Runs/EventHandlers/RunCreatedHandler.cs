// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Events;
using Caster.Api.Domain.Services;
using MediatR;

namespace Caster.Api.Features.Runs.EventHandlers
{
    public class RunCreatedHandler : INotificationHandler<RunCreated>
    {
        private readonly IRunQueueService _runQueueService;

        public RunCreatedHandler(IRunQueueService runQueueService)
        {
            _runQueueService = runQueueService;
        }

        public async Task Handle(RunCreated notification, CancellationToken cancellationToken)
        {
            _runQueueService.Add(new RunAdded { RunId = notification.RunId });
        }
    }
}
