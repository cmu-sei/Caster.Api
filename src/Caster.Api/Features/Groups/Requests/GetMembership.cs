// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using AutoMapper;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using Microsoft.EntityFrameworkCore;
using AutoMapper.QueryableExtensions;
using Caster.Api.Infrastructure.Authorization;
using FluentValidation;
using System.Linq;
using System.Text.Json.Serialization;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Groups
{
    public class GetMembership
    {
        [DataContract(Name = "GetGroupMembershipQuery")]
        public record Query : IRequest<GroupMembership>
        {
            /// <summary>
            /// Id of the GroupMembership.
            /// </summary>
            [JsonIgnore]
            public Guid Id { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, GroupMembership>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ViewGroups], cancellationToken);

            public override async Task<GroupMembership> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                var groupMembership = await dbContext.GroupMemberships
                    .Where(x => x.Id == request.Id)
                    .ProjectTo<GroupMembership>(mapper.ConfigurationProvider)
                    .FirstOrDefaultAsync(cancellationToken);

                if (groupMembership == null)
                    throw new EntityNotFoundException<GroupMembership>();

                return groupMembership;
            }
        }
    }
}
