// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Runtime.Serialization;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Data;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Caster.Api.Features.Shared;

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

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext, IIdentityResolver identityResolver) : BaseHandler<Command, Project>
        {
            public override async Task Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermissions.CreateProjects], cancellationToken);

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
