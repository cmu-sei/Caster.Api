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
using FluentValidation;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Domain.Models;
using System.Linq;
using AutoMapper.QueryableExtensions;
using System.Text.Json.Serialization;

namespace Caster.Api.Features.Groups
{
    public class CreateMembership
    {
        [DataContract(Name = "CreateGroupMembershipCommand")]
        public record Command : IRequest<GroupMembership>
        {
            /// <summary>
            /// The Id of the Group to add to.
            /// </summary>
            [JsonIgnore]
            public Guid GroupId { get; set; }

            /// <summary>
            /// The Id of the User to add.
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

        public class Handler(CasterContext _db, IMapper _mapper) : IRequestHandler<Command, GroupMembership>
        {
            public async Task<GroupMembership> Handle(Command request, CancellationToken cancellationToken)
            {
                var groupMembershipExists = await _db.GroupMemberships
                    .AnyAsync(x => x.GroupId == request.GroupId && x.UserId == request.UserId, cancellationToken);

                if (groupMembershipExists)
                    throw new ConflictException("User is already a member of this Group");

                var groupMembership = new Domain.Models.GroupMembership(request.GroupId, request.UserId);
                _db.GroupMemberships.Add(groupMembership);
                await _db.SaveChangesAsync();

                return _mapper.Map<GroupMembership>(groupMembership);
            }
        }
    }
}