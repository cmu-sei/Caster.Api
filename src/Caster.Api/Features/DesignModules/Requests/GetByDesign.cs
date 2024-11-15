// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using System.Text.Json.Serialization;
using System.Linq;
using Caster.Api.Features.Shared;
using FluentValidation;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;
using AutoMapper.QueryableExtensions;
using Caster.Api.Domain.Models;
using AutoMapper;
using Caster.Api.Data;

namespace Caster.Api.Features.DesignModules;

public class GetByDesign
{
    [DataContract(Name = "GetDesignModulesByDesignQuery")]
    public record Query : IRequest<DesignModule[]>
    {
        [JsonIgnore]
        public Guid DesignId { get; set; }
    }

    public class Validator : AbstractValidator<Query>
    {
        public Validator(IValidationService validationService)
        {
            RuleFor(x => x.DesignId).DesignExists(validationService);
        }
    }

    public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, DesignModule[]>
    {
        public override async Task Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<DesignModule>(request.DesignId, [SystemPermissions.ViewProjects], [ProjectPermissions.ViewProject], cancellationToken);

        public override async Task<DesignModule[]> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var designModules = await dbContext.DesignModules
                .Where(x => x.DesignId == request.DesignId)
                .ProjectTo<DesignModule>(mapper.ConfigurationProvider)
                .ToArrayAsync();

            return designModules;
        }
    }
}