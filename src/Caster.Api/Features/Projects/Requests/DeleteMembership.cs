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

        public class Handler(CasterContext _db) : IRequestHandler<Command>
        {
            public async Task Handle(Command request, CancellationToken cancellationToken)
            {
                var projectMembership = await _db.ProjectMemberships.FindAsync([request.Id], cancellationToken);

                if (projectMembership == null)
                    throw new EntityNotFoundException<ProjectMembership>();

                _db.ProjectMemberships.Remove(projectMembership);
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
