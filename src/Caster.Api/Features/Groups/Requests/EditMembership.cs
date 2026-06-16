// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using AutoMapper;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Groups
{
    public class EditMembership
    {
        [DataContract(Name = "EditGroupMembershipCommand")]
        public record Command : IRequest<GroupMembership>
        {
            /// <summary>
            /// The Id of the Group Membership to edit.
            /// </summary>
            [JsonIgnore]
            public Guid Id { get; set; }

            /// <summary>
            /// The User's role within the Group.
            /// </summary>
            [DataMember]
            public GroupMembershipRole Role { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Command, GroupMembership>
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Domain.Models.GroupMembership>(request.Id, [SystemPermission.ManageGroups], [GroupPermission.ManageMembership], cancellationToken);

            public override async Task<GroupMembership> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var groupMembership = await dbContext.GroupMemberships.FindAsync([request.Id], cancellationToken);

                if (groupMembership == null)
                    throw new EntityNotFoundException<GroupMembership>();

                groupMembership.Role = request.Role;
                await dbContext.SaveChangesAsync(cancellationToken);
                return mapper.Map<GroupMembership>(groupMembership);
            }
        }
    }
}
