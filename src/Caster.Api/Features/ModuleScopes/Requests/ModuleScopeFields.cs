// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Caster.Api.Domain.Services.ModuleScopes;
using FluentValidation;

namespace Caster.Api.Features.ModuleScopes;

public static class ModuleScopeFields
{
    public const int MaximumNameLength = 200;

    public static IRuleBuilderOptions<T, string> ValidName<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MaximumLength(MaximumNameLength);

    /// <summary>At least one path, each a valid module source name or folder of one.</summary>
    public static IRuleBuilderOptions<T, List<string>> ValidPaths<T>(this IRuleBuilder<T, List<string>> rule) =>
        rule
            .NotEmpty()
            .WithMessage("A Module Scope needs at least one path: a module source's name, or a folder of one such as cmu/exercise-x.")
            .Must(x => x == null || x.Count <= ModuleScopePaths.MaximumPaths)
            .WithMessage($"A Module Scope has at most {ModuleScopePaths.MaximumPaths} paths.")
            .Must(x => x == null || x.All(p => ModuleScopePaths.Normalize(p) is not null))
            .WithMessage("Each path is a module source's name, or a folder of one such as cmu/exercise-x: separated by slashes, with no wildcards (* or ?), backslashes, control characters, empty parts, '.' or '..'.");

    /// <summary>The paths trimmed, without repeats.</summary>
    public static List<string> Clean(IEnumerable<string> paths) =>
        paths.Select(ModuleScopePaths.Normalize).Distinct().ToList();
}
