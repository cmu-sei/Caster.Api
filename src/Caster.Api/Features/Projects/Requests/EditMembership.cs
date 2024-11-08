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
            [DataMember]
            public Guid Id { get; set; }

            [DataMember]
            public Guid? RoleId { get; set; }
        }

        public class Validator : AbstractValidator<Command>
        {
            public Validator(IValidationService validationService)
            {
                RuleFor(x => x.RoleId.Value).ProjectRoleExists(validationService).When(x => x.RoleId.HasValue);
            }
        }

        public class Handler(CasterContext _db, IMapper _mapper) : IRequestHandler<Command, ProjectMembership>
        {
            public async Task<ProjectMembership> Handle(Command request, CancellationToken cancellationToken)
            {
                var projectMembership = await _db.ProjectMemberships.FindAsync([request.Id], cancellationToken);

                if (projectMembership == null)
                    throw new EntityNotFoundException<ProjectMembership>();

                projectMembership.RoleId = request.RoleId;
                await _db.SaveChangesAsync(cancellationToken);
                return _mapper.Map<ProjectMembership>(projectMembership);
            }
        }
    }
}
