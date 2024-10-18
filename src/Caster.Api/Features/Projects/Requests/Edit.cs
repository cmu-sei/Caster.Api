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
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Projects
{
    public class Edit
    {
        [DataContract(Name = "EditProjectCommand")]
        public class Command : IRequest<Project>
        {
            public Guid Id { get; set; }

            /// <summary>
            /// Name of the project.
            /// </summary>
            [DataMember]
            public string Name { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Command, Project>
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Domain.Models.Project>(request.Id, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

            public override async Task<Project> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var project = await dbContext.Projects.FindAsync([request.Id], cancellationToken);

                if (project == null)
                    throw new EntityNotFoundException<Project>();

                mapper.Map(request, project);
                await dbContext.SaveChangesAsync(cancellationToken);
                return mapper.Map<Project>(project);
            }
        }
    }
}
