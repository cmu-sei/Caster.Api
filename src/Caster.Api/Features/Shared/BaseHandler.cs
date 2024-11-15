// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using MediatR;
using System.Threading.Tasks;
using System.Threading;

namespace Caster.Api.Features.Shared;

public abstract class BaseHandler<TRequest, TResponse> : IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken)
    {
        await this.Authorize(request, cancellationToken);
        return await this.HandleRequest(request, cancellationToken);
    }

    public abstract Task Authorize(TRequest request, CancellationToken cancellationToken);
    public abstract Task<TResponse> HandleRequest(TRequest request, CancellationToken cancellationToken);
}

public abstract class BaseHandler<TRequest> : IRequestHandler<TRequest>
    where TRequest : IRequest
{
    public async Task Handle(TRequest request, CancellationToken cancellationToken)
    {
        await this.Authorize(request, cancellationToken);
        await this.HandleRequest(request, cancellationToken);
    }

    public abstract Task Authorize(TRequest request, CancellationToken cancellationToken);
    public abstract Task HandleRequest(TRequest request, CancellationToken cancellationToken);
}