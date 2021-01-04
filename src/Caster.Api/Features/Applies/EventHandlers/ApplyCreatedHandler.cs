// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Events;
using Caster.Api.Domain.Services;
using MediatR;

namespace Caster.Api.Features.Applies.EventHandlers
{
    public class ApplyCreatedHandler : INotificationHandler<ApplyCreated>
    {
        private readonly IRunQueueService _runQueueService;

        public ApplyCreatedHandler(IRunQueueService runQueueService)
        {
            _runQueueService = runQueueService;
        }

        public async Task Handle(ApplyCreated notification, CancellationToken cancellationToken)
        {
            _runQueueService.Add(new ApplyAdded { ApplyId = notification.ApplyId });
        }
    }
}
