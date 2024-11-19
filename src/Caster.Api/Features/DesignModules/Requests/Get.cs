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
using AutoMapper.QueryableExtensions;
using Caster.Api.Domain.Models;
using AutoMapper;
using Caster.Api.Data;

namespace Caster.Api.Features.DesignModules;

public class Get
{
    [DataContract(Name = "GetDesignModuleQuery")]
    public record Query : IRequest<DesignModule>
    {
        [JsonIgnore]
        public Guid Id { get; set; }
    }

    public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, DesignModule>
    {
        public override async Task Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<Domain.Models.DesignModule>(request.Id, [SystemPermissions.ViewProjects], [ProjectPermissions.ViewProject], cancellationToken);

        public override async Task<DesignModule> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var designModule = await dbContext.DesignModules
                .Where(x => x.Id == request.Id)
                .ProjectTo<DesignModule>(mapper.ConfigurationProvider, null, membersToExpand: new[] { "ValuesJson" })
                .SingleOrDefaultAsync(cancellationToken);

            if (designModule == null)
                throw new EntityNotFoundException<DesignModule>();

            return designModule;
        }
    }
}