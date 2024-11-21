// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using Caster.Api.Infrastructure.Authorization;
using Microsoft.EntityFrameworkCore;
using FluentValidation;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;
using Project = Caster.Api.Features.Projects.Project;

namespace Caster.Api.Features.Vlan
{
    public class UnassignPartition
    {
        [DataContract(Name = "UnassignPartitionCommand")]
        public class Command : IRequest<Project>
        {
            /// <summary>
            /// The Id of the Project to unassign a partition from
            /// </summary>
            [DataMember]
            public Guid ProjectId { get; set; }
        }

        public class Validator : AbstractValidator<Command>
        {
            public Validator(IValidationService validationService)
            {
                RuleFor(x => x.ProjectId).ProjectExists(validationService);
            }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Command, Project>
        {
            public override async Task Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ManageVLANs], cancellationToken);

            public override async Task<Project> HandleRequest(Command command, CancellationToken cancellationToken)
            {
                var project = await dbContext.Projects
                    .Where(x => x.Id == command.ProjectId)
                    .FirstOrDefaultAsync(cancellationToken);

                project.PartitionId = null;
                await dbContext.SaveChangesAsync();

                return mapper.Map<Project>(project);
            }
        }
    }
}
