// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Shared;
using FluentValidation;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;
using AutoMapper;
using Caster.Api.Data;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Designs;

public class Create
{
    [DataContract(Name = "CreateDesignCommand")]
    public record Command : IRequest<Design>
    {
        /// <summary>
        /// Name of the design.
        /// </summary>
        [DataMember]
        public string Name { get; init; }

        /// <summary>
        /// ID of the directory this design is under.
        /// </summary>
        [DataMember]
        public Guid DirectoryId { get; init; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator(IValidationService validationService)
        {
            RuleFor(x => x.DirectoryId).DirectoryExists(validationService);
        }
    }

    public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Command, Design>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<Directory>(request.DirectoryId, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

        public override async Task<Design> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var design = mapper.Map<Domain.Models.Design>(request);

            dbContext.Designs.Add(design);
            await dbContext.SaveChangesAsync(cancellationToken);

            return mapper.Map<Design>(design);
        }
    }
}
