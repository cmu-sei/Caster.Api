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
using Caster.Api.Domain.Models;
using AutoMapper;
using Caster.Api.Data;

namespace Caster.Api.Features.DesignModules;

public class Create
{
    [DataContract(Name = "CreateDesignModuleCommand")]
    public record Command : IRequest<DesignModule>
    {
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
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<Design>(request.DesignId, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

        public override async Task<DesignModule> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var designModule = mapper.Map<Domain.Models.DesignModule>(request);

            dbContext.DesignModules.Add(designModule);
            await dbContext.SaveChangesAsync(cancellationToken);

            return mapper.Map<DesignModule>(designModule);
        }
    }
}
