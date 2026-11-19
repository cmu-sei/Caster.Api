// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.EnvironmentSets;
using Caster.Api.Features.Shared;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Extensions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.EnvironmentSets;

public class Assign
{
    [DataContract(Name = "AssignEnvironmentSetCommand")]
    public class Command : IRequest<ProjectEnvironmentSet[]>
    {
        public Guid ProjectId { get; set; }
        public Guid EnvironmentSetId { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator(IValidationService validationService)
        {
            RuleFor(x => x.ProjectId).ProjectExists(validationService);
        }
    }

    /// <summary>
    /// Assigns a set to a project: every workspace in it, including those Alloy creates, gets
    /// the set's variables on its next Terraform command.
    /// </summary>
    public class Handler(
        ICasterAuthorizationService authorizationService,
        CasterContext dbContext,
        ISecretSource secretSource,
        IEnvironmentSetViews views) : BaseHandler<Command, ProjectEnvironmentSet[]>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageEnvironmentSets], cancellationToken);

        public override Task<ProjectEnvironmentSet[]> HandleRequest(Command request, CancellationToken cancellationToken) =>
            dbContext.WithEnvironmentSetLock(() => Handle(request, cancellationToken), cancellationToken);

        private async Task<ProjectEnvironmentSet[]> Handle(Command request, CancellationToken cancellationToken)
        {
            var set = await dbContext.EnvironmentSets
                .AsNoTracking()
                .Include(x => x.Entries)
                .SingleOrDefaultAsync(x => x.Id == request.EnvironmentSetId, cancellationToken)
                ?? throw new EntityNotFoundException<EnvironmentSet>();

            if (await dbContext.ProjectEnvironmentSets.AnyAsync(
                x => x.ProjectId == request.ProjectId && x.EnvironmentSetId == set.Id, cancellationToken))
            {
                throw new ConflictException($"Environment Set \"{set.Name}\" is already assigned to this project.");
            }

            secretSource.EnsureAssignable(set);

            await dbContext.EnsureNoConflicts(set.Id, set.Entries.ToList(), [request.ProjectId], cancellationToken);

            dbContext.ProjectEnvironmentSets.Add(new Domain.Models.ProjectEnvironmentSet
            {
                ProjectId = request.ProjectId,
                EnvironmentSetId = set.Id
            });

            await dbContext.SaveChangesAsync(cancellationToken);

            return await views.GetProjectSets(request.ProjectId, cancellationToken);
        }
    }
}
