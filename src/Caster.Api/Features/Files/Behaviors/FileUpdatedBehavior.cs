// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Events;
using Caster.Api.Features.Files.Interfaces;
using MediatR;

namespace Caster.Api.Features.Files.Behaviors
{
    public class FileUpdatedBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IFileUpdateRequest
        where TResponse : File
    {
        private readonly IMediator _mediator;

        public FileUpdatedBehavior(IMediator mediator)
        {
            _mediator = mediator;
        }

        public async Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken, RequestHandlerDelegate<TResponse> next) {
            var response = await next();
            await _mediator.Publish(new FileUpdated(response.Id, request.UpdateContent));
            return response;
        }
    }
}
