// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Authorization;
using System.Text.Json.Serialization;
using System.Linq;
using Caster.Api.Features.Shared;
using FluentValidation;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;
using AutoMapper;
using Caster.Api.Data;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Designs;

public class GetByDirectory
{
    [DataContract(Name = "GetDesignsByDirectoryQuery")]
    public record Query : IRequest<Design[]>
    {
        [JsonIgnore]
        public Guid DirectoryId { get; set; }
    }

    public class Validator : AbstractValidator<Query>
    {
        public Validator(IValidationService validationService)
        {
            RuleFor(x => x.DirectoryId).DirectoryExists(validationService);
        }
    }

    public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, Design[]>
    {
        public override async Task Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<Directory>(request.DirectoryId, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], cancellationToken);

        public override async Task<Design[]> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var designs = await dbContext.Designs
                .Where(x => x.DirectoryId == request.DirectoryId)
                .ToArrayAsync();

            return mapper.Map<Design[]>(designs);
        }
    }
}