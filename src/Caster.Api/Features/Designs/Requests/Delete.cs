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
using System.Text.Json.Serialization;
using Caster.Api.Data;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Designs;

public class Delete
{
    [DataContract(Name = "DeleteDesignCommand")]
    public record Command : IRequest
    {
        [JsonIgnore]
        public Guid Id { get; set; }
    }

    public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext) : BaseHandler<Command>
    {
        public override async Task Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<Domain.Models.Design>(request.Id, [SystemPermissions.EditProjects], [ProjectPermissions.EditProject], cancellationToken);

        public override async Task HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var design = await dbContext.Designs.FindAsync(request.Id);

            if (design == null)
                throw new EntityNotFoundException<Design>();

            dbContext.Designs.Remove(design);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
