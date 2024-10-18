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
using FluentValidation;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Caster.Api.Domain.Models;
using AutoMapper;
using Caster.Api.Data;

namespace Caster.Api.Features.DesignModules;

public class AddOrUpdateValues
{
    [DataContract(Name = "AddOrUpdateValuesDesignModuleCommand")]
    public record Command : IRequest<DesignModule>
    {
        [JsonIgnore]
        public Guid DesignModuleId { get; set; }

        public ModuleValue[] Values { get; init; }
    }

    public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext)
        : BaseHandler<Command, DesignModule>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<Domain.Models.DesignModule>(
                request.DesignModuleId,
                [SystemPermission.EditProjects],
                [ProjectPermission.EditProject],
                cancellationToken);

        public override async Task<DesignModule> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var designModule = await dbContext.DesignModules
                .Where(x => x.Id == request.DesignModuleId)
                .SingleOrDefaultAsync(cancellationToken);

            if (designModule == null)
                throw new EntityNotFoundException<DesignModule>();

            designModule.AddOrUpdateValues(ModuleValue.ToDomain(request.Values));
            await dbContext.SaveChangesAsync(cancellationToken);

            return mapper.Map<DesignModule>(designModule);
        }
    }
}
