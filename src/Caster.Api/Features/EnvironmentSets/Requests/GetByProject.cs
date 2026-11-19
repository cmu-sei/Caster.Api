// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Shared;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Extensions;
using FluentValidation;
using MediatR;

namespace Caster.Api.Features.EnvironmentSets;

public class GetByProject
{
    [DataContract(Name = "GetProjectEnvironmentSetsQuery")]
    public class Query : IRequest<ProjectEnvironmentSet[]>
    {
        public Guid ProjectId { get; set; }
    }

    public class Validator : AbstractValidator<Query>
    {
        public Validator(IValidationService validationService)
        {
            RuleFor(x => x.ProjectId).ProjectExists(validationService);
        }
    }

    public class Handler(ICasterAuthorizationService authorizationService, IEnvironmentSetViews views) : BaseHandler<Query, ProjectEnvironmentSet[]>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<Project>(request.ProjectId, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], cancellationToken);

        public override async Task<ProjectEnvironmentSet[]> HandleRequest(Query request, CancellationToken cancellationToken) =>
            await views.GetProjectSets(request.ProjectId, cancellationToken);
    }
}
