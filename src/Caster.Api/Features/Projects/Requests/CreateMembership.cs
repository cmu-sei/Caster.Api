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

namespace Caster.Api.Features.Projects
{
    public class CreateMembership
    {
        [DataContract(Name = "CreateProjectMembershipCommand")]
        public record Command : IRequest<ProjectMembership>
        {
            /// <summary>
            /// The Id of the Project to add to.
            /// </summary>
            [JsonIgnore]
            public Guid ProjectId { get; set; }

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
                RuleFor(x => x.ProjectId).ProjectExists(validationService);
                RuleFor(x => x.UserId).UserExists(validationService);
            }
        }

        public class Handler(CasterContext _db, IMapper _mapper) : IRequestHandler<Command, ProjectMembership>
        {
            public async Task<ProjectMembership> Handle(Command request, CancellationToken cancellationToken)
            {
                var projectMembershipExists = await _db.ProjectMemberships
                    .AnyAsync(x => x.ProjectId == request.ProjectId && x.UserId == request.UserId, cancellationToken);

                if (projectMembershipExists)
                    throw new ConflictException("User is already a member of this Project");

                var projectMembership = new Domain.Models.ProjectMembership(request.ProjectId, request.UserId);
                _db.ProjectMemberships.Add(projectMembership);
                await _db.SaveChangesAsync();

                return _mapper.Map<ProjectMembership>(projectMembership);
            }
        }
    }
}
