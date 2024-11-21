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
using Caster.Api.Infrastructure.Authorization;
using FluentValidation;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

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
            public Guid RoleId { get; set; }
        }

        public class Validator : AbstractValidator<Command>
        {
            public Validator(IValidationService validationService)
            {
                RuleFor(x => x.RoleId).ProjectRoleExists(validationService);
            }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Command, ProjectMembership>
        {
            public override async Task Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Domain.Models.ProjectMembership>(request.Id, [SystemPermissions.EditProjects], [ProjectPermissions.EditProject], cancellationToken);

            public override async Task<ProjectMembership> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var projectMembership = await dbContext.ProjectMemberships.FindAsync([request.Id], cancellationToken);

                if (projectMembership == null)
                    throw new EntityNotFoundException<ProjectMembership>();

                projectMembership.RoleId = request.RoleId;
                await dbContext.SaveChangesAsync(cancellationToken);
                return mapper.Map<ProjectMembership>(projectMembership);
            }
        }
    }
}
