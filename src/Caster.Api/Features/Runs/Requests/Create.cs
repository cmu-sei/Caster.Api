// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using AutoMapper;
using System.Runtime.Serialization;
using Caster.Api.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Domain.Events;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Domain.Services;
using Caster.Api.Infrastructure.Identity;
using AutoMapper.QueryableExtensions;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Features.Shared;
using FluentValidation;
using Caster.Api.Features.Shared.Services;

namespace Caster.Api.Features.Runs
{
    public class Create
    {
        [DataContract(Name = "CreateRunCommand")]
        public class Command : IRequest<Run>
        {
            /// <summary>
            /// The Id of the Workspace to create the Run in
            /// </summary>
            [DataMember]
            public Guid WorkspaceId { get; set; }

            /// <summary>
            /// If true, will create a Run to destroy all resources in the Workspace
            /// </summary>
            [DataMember]
            public bool IsDestroy { get; set; }

            /// <summary>
            /// Optional list of resources to constrain the affects of this Run to
            /// </summary>
            [DataMember]
            public string[] Targets { get; set; }
        }

        public class CommandValidator : AbstractValidator<Command>
        {
            public CommandValidator(IValidationService validationService)
            {
                RuleFor(x => x.WorkspaceId).WorkspaceExists(validationService);
            }
        }

        public class Handler(
            ICasterAuthorizationService authorizationService,
            IMapper mapper,
            CasterContext dbContext,
            IMediator mediator,
            ILockService lockService,
            IIdentityResolver identityResolver) : BaseHandler<Command, Run>
        {
            public override async Task Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Domain.Models.Workspace>(request.WorkspaceId, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

            public override async Task<Run> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                Domain.Models.Run run = null;

                using (var lockResult = await lockService.GetWorkspaceLock(request.WorkspaceId).LockAsync(0))
                {
                    if (!lockResult.AcquiredLock)
                        throw new WorkspaceConflictException();

                    if (await dbContext.AnyIncompleteRuns(request.WorkspaceId))
                    {
                        throw new ConflictException("This Workspace's current Run must be rejected or applied before a new one can be created.");
                    }

                    run = await this.DoWork(request, identityResolver.GetClaimsPrincipal().GetId(), cancellationToken);
                }

                await mediator.Publish(new RunCreated { RunId = run.Id });

                return await dbContext.Runs
                    .ProjectTo<Run>(mapper.ConfigurationProvider)
                    .SingleOrDefaultAsync(x => x.Id == run.Id, cancellationToken);
            }

            private async Task<Domain.Models.Run> DoWork(Command request, Guid userId, CancellationToken cancellationToken)
            {
                var run = mapper.Map<Domain.Models.Run>(request);
                run.CreatedById = userId;
                run.Modify(userId);
                dbContext.Runs.Add(run);
                await dbContext.SaveChangesAsync(cancellationToken);
                return run;
            }
        }
    }
}
