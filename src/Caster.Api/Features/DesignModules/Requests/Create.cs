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

    public class Handler : BaseHandler<Handler>, IRequestHandler<Command, DesignModule>
    {
        public Handler(IDependencyAggregate<Handler> aggregate) : base(aggregate) { }

        public async Task<DesignModule> Handle(Command request, CancellationToken cancellationToken)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                throw new ForbiddenException();

            var designModule = _mapper.Map<Domain.Models.DesignModule>(request);

            _db.DesignModules.Add(designModule);
            await _db.SaveChangesAsync(cancellationToken);

            return _mapper.Map<DesignModule>(designModule);
        }
    }
}
