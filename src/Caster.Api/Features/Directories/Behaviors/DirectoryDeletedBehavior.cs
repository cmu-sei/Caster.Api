// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Events;
using Caster.Api.Features.Directories.Interfaces;
using Caster.Api.Features.Files.Interfaces;
using MediatR;

namespace Caster.Api.Features.Directories.Behaviors
{
    public class DirectoryDeletedBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IDirectoryDeleteRequest
    {
        private readonly IMediator _mediator;
        private readonly CasterContext _db;

        public DirectoryDeletedBehavior(IMediator mediator, CasterContext db)
        {
            _mediator = mediator;
            _db = db;
        }

        public async Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken, RequestHandlerDelegate<TResponse> next) {
            var directory = await _db.Directories.FindAsync(request.Id);
            var response = await next();
            await _mediator.Publish(new DirectoryDeleted(directory));
            return response;
        }
    }
}
