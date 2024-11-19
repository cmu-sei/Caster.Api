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
using Caster.Api.Domain.Models;
using Caster.Api.Data;
using AutoMapper;

namespace Caster.Api.Features.DesignModules;

public class Edit
{
    [DataContract(Name = "EditDesignModuleCommand")]
    public record Command : IRequest<DesignModule>
    {
        [JsonIgnore]
        public Guid DesignModuleId { get; set; }

        /// <summary>
        /// The Id of the Design to add this DesignModule
        /// </summary>
        public Guid DesignId { get; set; }

        /// <summary>
        /// The Id of the selected Module for this DesignModule
        /// </summary>
        public Guid ModuleId { get; init; }

        /// <summary>
        /// Name of the DesignModule.
        /// </summary>
        [DataMember]
        public string Name { get; init; }

        /// <summary>
        /// Version of the selected Module to use
        /// </summary>
        [DataMember]
        public string ModuleVersion { get; init; }

        /// <summary>
        /// Values for each input
        /// </summary>
        [DataMember]
        public ModuleValue[] Values { get; init; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator(IValidationService validationService)
        {
            RuleFor(x => x.DesignId).DesignExists(validationService);
            RuleFor(x => x.ModuleId).ModuleExists(validationService);
        }
    }

    public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Command, DesignModule>
    {
        public async override Task Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<Domain.Models.DesignModule>(request.DesignModuleId, [SystemPermissions.EditProjects], [ProjectPermissions.EditProject], cancellationToken);

        public override async Task<DesignModule> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var designModule = await dbContext.DesignModules
                .Where(x => x.Id == request.DesignModuleId)
                .SingleOrDefaultAsync(cancellationToken);

            if (designModule == null)
                throw new EntityNotFoundException<DesignModule>();

            mapper.Map(request, designModule);
            await dbContext.SaveChangesAsync(cancellationToken);

            return mapper.Map<DesignModule>(designModule);
        }
    }
}
