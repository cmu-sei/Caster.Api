// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using AutoMapper.QueryableExtensions;
using Caster.Api.Data;
using Caster.Api.Infrastructure.Authorization;
using System;
using FluentValidation;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;
using System.Linq;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Groups
{
    public class GetMemberships
    {
        [DataContract(Name = "GetGroupMembershipsQuery")]
        public record Query : IRequest<GroupMembership[]>
        {
            public Guid GroupId { get; set; }
        }

        public class Validator : AbstractValidator<Query>
        {
            public Validator(IValidationService validationService)
            {
                RuleFor(x => x.GroupId).GroupExists(validationService);
            }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, GroupMembership[]>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ViewGroups], cancellationToken);

            public override async Task<GroupMembership[]> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                return await dbContext.GroupMemberships
                    .Where(x => x.GroupId == request.GroupId)
                    .ProjectTo<GroupMembership>(mapper.ConfigurationProvider)
                    .ToArrayAsync();
            }
        }
    }
}
