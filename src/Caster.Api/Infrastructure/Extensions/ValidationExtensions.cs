/*
Copyright 2021 Carnegie Mellon University. All Rights Reserved. 
 Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
*/

using Caster.Api.Features.Shared.Services;
using FluentValidation;
using System;

namespace Caster.Api.Infrastructure.Extensions;

public static class ValidationExtensions
{
    public static IRuleBuilderOptions<T, Guid> DirectoryExists<T>(this IRuleBuilder<T, Guid> ruleBuilder, IValidationService validationService)
    {
        return ruleBuilder
            .MustAsync(async (id, cancellationToken) => await validationService.DirectoryExists(id))
            .WithMessage("Directory does not exist");
    }

    public static IRuleBuilderOptions<T, Guid> DesignExists<T>(this IRuleBuilder<T, Guid> ruleBuilder, IValidationService validationService)
    {
        return ruleBuilder
            .MustAsync(async (id, cancellationToken) => await validationService.DesignExists(id))
            .WithMessage("Design does not exist");
    }

    public static IRuleBuilderOptions<T, Guid?> DesignExists<T>(this IRuleBuilder<T, Guid?> ruleBuilder, IValidationService validationService)
    {
        return ruleBuilder
            .MustAsync(async (id, cancellationToken) => id.HasValue ? await validationService.DesignExists(id.Value) : true)
            .WithMessage("Design does not exist");
    }

    public static IRuleBuilderOptions<T, Guid> ModuleExists<T>(this IRuleBuilder<T, Guid> ruleBuilder, IValidationService validationService)
    {
        return ruleBuilder
            .MustAsync(async (id, cancellationToken) => await validationService.ModuleExists(id))
            .WithMessage("Module does not exist");
    }
}