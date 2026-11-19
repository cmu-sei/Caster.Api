// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Domain.Services.EnvironmentSets;
using Caster.Api.Domain.Services.Terraform;
using Caster.Api.Features.Shared;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Options;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.EnvironmentSets;

public class GetSecrets
{
    [DataContract(Name = "GetEnvironmentSecretsQuery")]
    public class Query : IRequest<EnvironmentSecrets>
    {
    }

    public class Handler(
        ICasterAuthorizationService authorizationService,
        CasterContext dbContext,
        ISecretSource secretSource,
        IEntryValues entryValues,
        TerraformOptions terraformOptions,
        ITerraformService terraformService) : BaseHandler<Query, EnvironmentSecrets>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ViewEnvironmentSets], cancellationToken);

        public override async Task<EnvironmentSecrets> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var used = await dbContext.EnvironmentSetEntries
                .Where(x => x.ValueSource == EnvironmentSetValueSource.Secret && x.SecretName != null)
                .Select(x => x.SecretName)
                .Distinct()
                .ToListAsync(cancellationToken);

            return new EnvironmentSecrets
            {
                Available = secretSource.GetAvailableNames(),
                Missing = used
                    .Where(x => !secretSource.IsAvailable(x))
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToArray(),
                Patterns = (terraformOptions.Secrets?.Patterns ?? [])
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToArray(),
                // The mode is chosen once, at startup; report the one in use rather than
                // the current setting, which a configuration reload can change.
                RunMode = terraformService is KubernetesTerraformService ? TerraformRunMode.Kubernetes : TerraformRunMode.Process,
                InheritAll = terraformOptions.EnvironmentVariables?.InheritAll == true,
                MinimumMaskedLength = entryValues.MinimumMaskedLength,
                TooShort = secretSource.GetAvailableNames()
                    .Where(x => secretSource.GetValue(x)?.Length < entryValues.MinimumMaskedLength)
                    .ToArray()
            };
        }
    }
}
