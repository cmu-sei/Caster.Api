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
using FluentValidation;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.DesignModules;

public class SetEnabled
{
    [DataContract(Name = "SetEnabledDesignModuleCommand")]
    public record Command : IRequest<DesignModule>
    {
        [JsonIgnore]
        public Guid DesignModuleId { get; set; }

        public bool Enabled { get; init; }
    }

    public class Handler : BaseHandler<Handler>, IRequestHandler<Command, DesignModule>
    {
        public Handler(IDependencyAggregate<Handler> aggregate) : base(aggregate) { }

        public async Task<DesignModule> Handle(Command request, CancellationToken cancellationToken)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                throw new ForbiddenException();

            var designModule = await _db.DesignModules
                .Where(x => x.Id == request.DesignModuleId)
                .SingleOrDefaultAsync(cancellationToken);

            if (designModule == null)
                throw new EntityNotFoundException<DesignModule>();

            designModule.Enabled = request.Enabled;
            await _db.SaveChangesAsync(cancellationToken);

            return _mapper.Map<DesignModule>(designModule);
        }
    }
}
