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
using System.Text.Json.Serialization;
using Caster.Api.Features.Shared;
using Caster.Api.Infrastructure.Authorization;

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
            public Guid? UserId { get; set; }

            /// <summary>
            /// The Id of the Group to add.
            /// </summary>
            [DataMember]
            public Guid? GroupId { get; set; }
        }

        public class Validator : AbstractValidator<Command>
        {
            public Validator(IValidationService validationService)
            {
                RuleFor(x => x.ProjectId).ProjectExists(validationService);
                RuleFor(x => x.UserId.Value).UserExists(validationService).When(x => x.UserId.HasValue);
                RuleFor(x => x.GroupId.Value).GroupExists(validationService).When(x => x.GroupId.HasValue);
            }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Command, ProjectMembership>
        {
            public override async Task Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Domain.Models.Project>(request.ProjectId, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

            public override async Task<ProjectMembership> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var projectMembershipExists = await dbContext.ProjectMemberships
                    .AnyAsync(x => x.ProjectId == request.ProjectId && x.UserId == request.UserId && x.GroupId == request.GroupId, cancellationToken);

                if (projectMembershipExists)
                    throw new ConflictException("ProjectMembership already exists");

                var projectMembership = new Domain.Models.ProjectMembership(request.ProjectId, request.UserId, request.GroupId);
                dbContext.ProjectMemberships.Add(projectMembership);
                await dbContext.SaveChangesAsync(cancellationToken);

                return mapper.Map<ProjectMembership>(projectMembership);
            }
        }
    }
}
