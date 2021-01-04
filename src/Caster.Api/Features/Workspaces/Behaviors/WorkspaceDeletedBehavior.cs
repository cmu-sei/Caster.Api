// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Events;
using Caster.Api.Features.Workspaces.Interfaces;
using MediatR;

namespace Caster.Api.Features.Workspaces.Behaviors
{
    public class WorkspaceDeletedBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IWorkspaceDeleteRequest
    {
        private readonly CasterContext _db;
        private readonly IMediator _mediator;

        public WorkspaceDeletedBehavior(CasterContext db, IMediator mediator)
        {
            _db = db;
            _mediator = mediator;
        }

        public async Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken, RequestHandlerDelegate<TResponse> next) {
            var workspace = await _db.Workspaces.FindAsync(request.Id);
            var response = await next();
            await _mediator.Publish(new WorkspaceDeleted(workspace));
            return response;
        }
    }
}
