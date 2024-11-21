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
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using AutoMapper;
using Caster.Api.Data;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Designs;

public class Edit
{
    [DataContract(Name = "EditDesignCommand")]
    public record Command : IRequest<Design>
    {
        [JsonIgnore]
        public Guid Id { get; set; }

        /// <summary>
        /// Name of the design.
        /// </summary>
        [DataMember]
        public string Name { get; init; }
    }

    public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Command, Design>
    {
        public override async Task Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<Domain.Models.Design>(request.Id, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

        public override async Task<Design> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var design = await dbContext.Designs
                .Where(x => x.Id == request.Id)
                .SingleOrDefaultAsync(cancellationToken);

            if (design == null)
                throw new EntityNotFoundException<Design>();

            mapper.Map(request, design);

            await dbContext.SaveChangesAsync(cancellationToken);

            return mapper.Map<Design>(design);
        }
    }
}
