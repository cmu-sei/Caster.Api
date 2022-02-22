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

    public class Handler : BaseHandler<Handler>, IRequestHandler<Query, DesignModule[]>
    {
        public Handler(IDependencyAggregate<Handler> aggregate) : base(aggregate) { }

        public async Task<DesignModule[]> Handle(Query request, CancellationToken cancellationToken)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                throw new ForbiddenException();

            var designModules = await _db.DesignModules
                .Where(x => x.DesignId == request.DesignId)
                .ProjectTo<DesignModule>(_mapper.ConfigurationProvider)
                .ToArrayAsync();

            return designModules;
        }
    }
}