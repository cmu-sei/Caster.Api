// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Shared;
using System.Text.Json.Serialization;

namespace Caster.Api.Features.Variables;

public class Delete
{
    [DataContract(Name = "DeleteVariableCommand")]
    public record Command : IRequest
    {
        [JsonIgnore]
        public Guid Id { get; set; }
    }

    public class Handler : BaseHandler<Handler>, IRequestHandler<Command>
    {
        public Handler(IDependencyAggregate<Handler> aggregate) : base(aggregate) { }

        public async Task Handle(Command request, CancellationToken cancellationToken)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                throw new ForbiddenException();

            var variable = await _db.Variables.FindAsync(request.Id);

            if (variable == null)
                throw new EntityNotFoundException<Variable>();

            _db.Variables.Remove(variable);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
