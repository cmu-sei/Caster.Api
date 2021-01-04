// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Events;
using Caster.Api.Features.Directories.Interfaces;
using MediatR;

namespace Caster.Api.Features.Directories.Behaviors
{
    public class DirectoryUpdatedBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IDirectoryUpdateRequest
        where TResponse : Directory
    {
        private readonly IMediator _mediator;

        public DirectoryUpdatedBehavior(IMediator mediator)
        {
            _mediator = mediator;
        }

        public async Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken, RequestHandlerDelegate<TResponse> next) {
            var response = await next();
            await _mediator.Publish(new DirectoryUpdated(response.Id));
            return response;
        }
    }
}
