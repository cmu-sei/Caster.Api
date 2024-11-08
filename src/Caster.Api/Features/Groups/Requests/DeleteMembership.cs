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

namespace Caster.Api.Features.Groups
{
    public class DeleteMembership
    {
        [DataContract(Name = "DeleteGroupMembershipCommand")]
        public record Command : IRequest
        {
            /// <summary>
            /// The Id of the Group to delete from.
            /// </summary>
            [JsonIgnore]
            public Guid GroupId { get; set; }

            /// <summary>
            /// The Id of the User to remove.
            /// </summary>
            [DataMember]
            public Guid UserId { get; set; }
        }

        public class Validator : AbstractValidator<Command>
        {
            public Validator(IValidationService validationService)
            {
                RuleFor(x => x.GroupId).GroupExists(validationService);
                RuleFor(x => x.UserId).UserExists(validationService);
            }
        }

        public class Handler(CasterContext _db) : IRequestHandler<Command>
        {
            public async Task Handle(Command request, CancellationToken cancellationToken)
            {
                var groupMembership = await _db.GroupMemberships
                    .Where(x => x.GroupId == request.GroupId && x.UserId == request.UserId)
                    .FirstOrDefaultAsync(cancellationToken);

                _db.GroupMemberships.Remove(groupMembership);
                await _db.SaveChangesAsync();
            }
        }
    }
}
