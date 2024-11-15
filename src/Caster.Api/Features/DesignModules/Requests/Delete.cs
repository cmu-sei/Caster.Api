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
using Caster.Api.Domain.Models;
using Caster.Api.Data;

namespace Caster.Api.Features.DesignModules;

public class Delete
{
    [DataContract(Name = "DeleteDesignModuleCommand")]
    public record Command : IRequest
    {
        [JsonIgnore]
        public Guid Id { get; set; }
    }

    public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext) : BaseHandler<Command>, IRequestHandler<Command>
    {
        public async override Task Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<DesignModule>(request.Id, [SystemPermissions.EditProjects], [ProjectPermissions.EditProject], cancellationToken);

        public async override Task HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var designModule = await dbContext.DesignModules.FindAsync(request.Id);

            if (designModule == null)
                throw new EntityNotFoundException<DesignModule>();

            dbContext.DesignModules.Remove(designModule);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
