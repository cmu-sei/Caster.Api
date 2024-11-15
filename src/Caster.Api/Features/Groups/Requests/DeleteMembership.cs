// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using System.Runtime.Serialization;
using Microsoft.EntityFrameworkCore;
using FluentValidation;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;
using System.Linq;
using System.Text.Json.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Features.Shared;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Groups
{
    public class DeleteMembership
    {
        [DataContract(Name = "DeleteGroupMembershipCommand")]
        public record Command : IRequest
        {
            /// <summary>
            /// The Id of the Group Membership to delete
            /// </summary>
            [JsonIgnore]
            public Guid Id { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext) : BaseHandler<Command>
        {
            public override async Task Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermissions.EditGroups], cancellationToken);

            public override async Task HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var groupMembership = await dbContext.GroupMemberships.FindAsync([request.Id], cancellationToken);

                if (groupMembership == null)
                    throw new EntityNotFoundException<GroupMembership>();

                dbContext.GroupMemberships.Remove(groupMembership);
                await dbContext.SaveChangesAsync();
            }
        }
    }
}
