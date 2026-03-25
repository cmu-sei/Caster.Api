// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Runtime.Serialization;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Domain.Services;
using System.Linq;
using AutoMapper.QueryableExtensions;
using System.Text.Json.Serialization;
using Caster.Api.Features.Shared;

namespace Caster.Api.Features.Runs
{
    public class Cancel
    {
        [DataContract(Name = "CancelRunCommand")]
        public class Command : IRequest<Run>
        {
            /// <summary>
            /// The Id of the Run to Cancel
            /// </summary>
            [JsonIgnore]
            public Guid Id { get; set; }

            /// <summary>
            /// If true, will forcefully terminate the Run.
            /// If false, will attempt to gracefully cancel the Run.
            /// </summary>
            [DataMember]
            public bool Force { get; set; }
        }

        public class Handler(
            ICasterAuthorizationService authorizationService,
            IMapper mapper,
            CasterContext dbContext,
            ITerraformService terraformService,
            IRunQueueService runQueueService) : BaseHandler<Command, Run>
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Domain.Models.Run>(request.Id, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

            public override async Task<Run> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var run = await dbContext.Runs
                    .Where(x => x.Id == request.Id)
                    .Include(x => x.Workspace)
                    .SingleOrDefaultAsync(cancellationToken);

                ValidateRun(run);

                if (RunHelpers.QueuedStatuses.Contains(run.Status))
                {
                    runQueueService.Cancel(run.Id);
                    run.Status = RunStatus.Rejected;
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                else
                {
                    await terraformService.CancelRun(run.Workspace, request.Force);
                }

                return await dbContext.Runs
                    .ProjectTo<Run>(mapper.ConfigurationProvider)
                    .SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
            }

            private static void ValidateRun(Domain.Models.Run run)
            {
                if (run == null)
                    throw new EntityNotFoundException<Run>();

                if (!RunHelpers.ActiveStatuses.Contains(run.Status))
                {
                    throw new InvalidOperationException("Cannot cancel a Run that is not queued or in progress");
                }
            }
        }
    }
}
