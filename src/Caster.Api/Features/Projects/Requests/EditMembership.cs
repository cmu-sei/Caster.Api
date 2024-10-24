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
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using AutoMapper.QueryableExtensions;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;
using FluentValidation;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;
using System.Linq;
using System.Text.Json.Serialization;

namespace Caster.Api.Features.Projects
{
    public class EditMembership
    {
        [DataContract(Name = "EditProjectMembershipCommand")]
        public record Command : IRequest<ProjectMembership>
        {
            /// <summary>
            /// The Project Id of the Membership
            /// </summary>
            [JsonIgnore]
            public Guid ProjectId { get; set; }

            /// <summary>
            /// The User Id of the Membership
            /// </summary>
            [DataMember]
            public Guid UserId { get; set; }

            [DataMember]
            public Guid? RoleId { get; set; }
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
                var projectMembership = await _db.ProjectMemberships
                    .Include(x => x.Role)
                    .Where(x => x.ProjectId == request.ProjectId && x.UserId == request.UserId)
                    .FirstOrDefaultAsync(cancellationToken);

                if (projectMembership == null)
                    throw new EntityNotFoundException<ProjectMembership>();

                projectMembership.RoleId = request.RoleId;
                await _db.SaveChangesAsync();
                return _mapper.Map<ProjectMembership>(projectMembership);
            }
        }
    }
}
