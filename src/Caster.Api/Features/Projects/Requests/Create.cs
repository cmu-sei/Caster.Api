// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Runtime.Serialization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using System.Collections.Generic;
using Caster.Api.Data;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Shared;
using Microsoft.EntityFrameworkCore;
using System;
using Caster.Api.Domain.Services;
using FluentValidation;

namespace Caster.Api.Features.Projects
{
    public class Create
    {
        [DataContract(Name = "CreateProjectCommand")]
        public class Command : IRequest<Project>
        {
            /// <summary>
            /// Name of the project.
            /// </summary>
            [DataMember]
            public string Name { get; set; }
        }

        public class CommandValidator : AbstractValidator<Command>
        {
            public CommandValidator()
            {
                RuleFor(x => x.Name)
                    .NotEmpty()
                    .WithMessage("Project Name is required and cannot be empty.");
            }
        }

        public class Handler(
            ICasterAuthorizationService authorizationService,
            IMapper mapper,
            CasterContext dbContext,
            IIdentityResolver identityResolver) : BaseHandler<Command, Project>
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.CreateProjects], cancellationToken);

            public override async Task<Project> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var project = mapper.Map<Domain.Models.Project>(request);
                dbContext.Projects.Add(project);

                // Add the creator as a member with the appropriate role
                var projectMembership = new Domain.Models.ProjectMembership();
                projectMembership.UserId = identityResolver.GetClaimsPrincipal().GetId();
                projectMembership.Project = project;
                projectMembership.RoleId = ProjectRoleDefaults.ProjectCreatorRoleId;
                dbContext.ProjectMemberships.Add(projectMembership);

                await dbContext.SaveChangesAsync(cancellationToken);

                return mapper.Map<Project>(project);
            }
        }
    }
}
