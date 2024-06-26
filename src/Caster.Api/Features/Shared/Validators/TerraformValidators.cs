// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Caster.Api.Infrastructure.Options;
using FluentValidation;

namespace Caster.Api.Features.Shared.Validators;

public static class TerraformValidationRules
{
    public static IRuleBuilderOptions<T, int> ParalellismValidation<T>(this IRuleBuilder<T, int> rule, TerraformOptions options)
    {
        return rule
            .GreaterThan(0)
            .LessThan(options.MaxParallelism);
    }

    public static IRuleBuilderOptions<T, int> AzureThresholdValidation<T>(this IRuleBuilder<T, int> rule)
    {
        return rule
            .GreaterThan(0)
            .LessThanOrEqualTo(10);
    }
}