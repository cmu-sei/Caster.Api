// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Features.Shared;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Projects
{
    public class DeleteMembership
    {
        [DataContract(Name = "DeleteProjectMembershipCommand")]
        public record Command : IRequest
        {
            /// <summary>
            /// The Id of the ProjectMembership to delete.
            /// </summary>
            [JsonIgnore]
            public Guid Id { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext) : BaseHandler<Command>
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Domain.Models.ProjectMembership>(request.Id, [SystemPermission.ManageProjects], [ProjectPermission.ManageProject], cancellationToken);

            public override async Task HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var projectMembership = await dbContext.ProjectMemberships.FindAsync([request.Id], cancellationToken);

                if (projectMembership == null)
                    throw new EntityNotFoundException<ProjectMembership>();

                dbContext.ProjectMemberships.Remove(projectMembership);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
