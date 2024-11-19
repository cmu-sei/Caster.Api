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

namespace Caster.Api.Features.Variables;

public class Get
{
    [DataContract(Name = "GetVariableQuery")]
    public record Query : IRequest<Variable>
    {
        [JsonIgnore]
        public Guid Id { get; set; }
    }

    public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, Variable>
    {
        public override async Task Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<Domain.Models.Variable>(request.Id, [SystemPermissions.ViewProjects], [ProjectPermissions.ViewProject], cancellationToken);

        public override async Task<Variable> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var variable = await dbContext.Variables
                .Where(x => x.Id == request.Id)
                .ProjectTo<Variable>(mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(cancellationToken);

            if (variable == null)
                throw new EntityNotFoundException<Variable>();

            return variable;
        }
    }
}