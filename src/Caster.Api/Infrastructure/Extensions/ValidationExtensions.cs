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

    public static IRuleBuilderOptions<T, Guid> ModuleExists<T>(this IRuleBuilder<T, Guid> ruleBuilder, IValidationService validationService)
    {
        return ruleBuilder
            .MustAsync(async (id, cancellationToken) => await validationService.ModuleExists(id))
            .WithMessage("Module does not exist");
    }

    public static IRuleBuilderOptions<T, Guid> ProjectExists<T>(this IRuleBuilder<T, Guid> ruleBuilder, IValidationService validationService)
    {
        return ruleBuilder
            .MustAsync(async (id, cancellationToken) => await validationService.ProjectExists(id))
            .WithMessage("Project does not exist");
    }

    public static IRuleBuilderOptions<T, Guid> PartitionExists<T>(this IRuleBuilder<T, Guid> ruleBuilder, IValidationService validationService)
    {
        return ruleBuilder
            .MustAsync(async (id, cancellationToken) => await validationService.PartitionExists(id))
            .WithMessage("Partition does not exist");
    }

    public static IRuleBuilderOptions<T, Guid> PoolExists<T>(this IRuleBuilder<T, Guid> ruleBuilder, IValidationService validationService)
    {
        return ruleBuilder
            .MustAsync(async (id, cancellationToken) => await validationService.PoolExists(id))
            .WithMessage("Pool does not exist");
    }

    public static IRuleBuilderOptions<T, Guid> VlanExists<T>(this IRuleBuilder<T, Guid> ruleBuilder, IValidationService validationService)
    {
        return ruleBuilder
            .MustAsync(async (id, cancellationToken) => await validationService.VlanExists(id))
            .WithMessage("VLAN does not exist");
    }

    public static IRuleBuilderOptions<T, Guid> WorkspaceExists<T>(this IRuleBuilder<T, Guid> ruleBuilder, IValidationService validationService)
    {
        return ruleBuilder
            .MustAsync(async (id, cancellationToken) => await validationService.WorkspaceExists(id))
            .WithMessage("Workspace does not exist");
    }
}