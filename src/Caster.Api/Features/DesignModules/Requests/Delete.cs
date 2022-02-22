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

namespace Caster.Api.Features.DesignModules;

public class Delete
{
    [DataContract(Name = "DeleteDesignModuleCommand")]
    public record Command : IRequest<Unit>
    {
        [JsonIgnore]
        public Guid Id { get; set; }
    }

    public class Handler : BaseHandler<Handler>, IRequestHandler<Command>
    {
        public Handler(IDependencyAggregate<Handler> aggregate) : base(aggregate) { }

        public async Task<Unit> Handle(Command request, CancellationToken cancellationToken)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                throw new ForbiddenException();

            var designModule = await _db.DesignModules.FindAsync(request.Id);

            if (designModule == null)
                throw new EntityNotFoundException<DesignModule>();

            _db.DesignModules.Remove(designModule);
            await _db.SaveChangesAsync(cancellationToken);

            return Unit.Value;
        }
    }
}
