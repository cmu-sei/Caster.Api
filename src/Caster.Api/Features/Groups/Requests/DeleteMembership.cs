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

        public class Handler(CasterContext _db) : IRequestHandler<Command>
        {
            public async Task Handle(Command request, CancellationToken cancellationToken)
            {
                var groupMembership = await _db.GroupMemberships.FindAsync([request.Id], cancellationToken);

                if (groupMembership == null)
                    throw new EntityNotFoundException<GroupMembership>();

                _db.GroupMemberships.Remove(groupMembership);
                await _db.SaveChangesAsync();
            }
        }
    }
}
