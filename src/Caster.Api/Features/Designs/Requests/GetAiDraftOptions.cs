// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Shared;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Infrastructure.Options;
using FluentValidation;
using MediatR;

namespace Caster.Api.Features.Designs;

public class GetAiDraftOptions
{
    [DataContract(Name = "GetAiDesignDraftOptionsQuery")]
    public record Query : IRequest<AiDesignDraftOptions>
    {
        [JsonIgnore]
        public Guid DirectoryId { get; set; }
    }

    public class Validator : AbstractValidator<Query>
    {
        public Validator(IValidationService validationService)
        {
            RuleFor(x => x.DirectoryId).DirectoryExists(validationService);
        }
    }

    public class Handler(ICasterAuthorizationService authorizationService, AiOptions aiOptions, TerraformOptions terraformOptions) : BaseHandler<Query, AiDesignDraftOptions>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<Directory>(request.DirectoryId, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

        public override Task<AiDesignDraftOptions> HandleRequest(Query request, CancellationToken cancellationToken) =>
            Task.FromResult(new AiDesignDraftOptions
            {
                Enabled = aiOptions.Enabled,
                ConfiguredHypervisors = Hypervisors.Configured(terraformOptions)
            });
    }
}
