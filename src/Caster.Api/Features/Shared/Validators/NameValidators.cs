// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using Caster.Api.Domain.Services;
using FluentValidation;

namespace Caster.Api.Features.Shared.Validators;

public static class NameValidationRules
{
    /// <summary>
    /// Segments that Caster uses for its own purposes inside an archive. A Directory or
    /// Workspace using one of these names would collide with them on export and import.
    /// </summary>
    public static readonly string[] ReservedNames =
    [
        ArchiveService.WorkspacesEntryName,
        ArchiveService.ReservedEntryName
    ];

    /// <summary>
    /// Directory names are interpolated directly into archive entry paths, so an unvalidated
    /// name can put traversal sequences into an artifact handed to users. The allowlist is the
    /// same one used for Workspace names.
    /// </summary>
    public static IRuleBuilderOptions<T, string> DirectoryNameValidation<T>(this IRuleBuilder<T, string> rule)
    {
        return rule.ArchiveSafeNameValidation("Directory");
    }

    /// <summary>
    /// Rejects only the names that Caster reserves, for use where an allowlist is already
    /// applied elsewhere. Workspace names already have the allowlist applied through
    /// IWorkspaceUpdateRequest, but nothing stopped them from taking a reserved name.
    /// </summary>
    public static IRuleBuilderOptions<T, string> NotAReservedName<T>(this IRuleBuilder<T, string> rule, string entityName)
    {
        return rule
            .Must(x => string.IsNullOrEmpty(x) || (x != "." && x != ".." && !IsReservedName(x)))
            .WithMessage($"{entityName} names cannot be . or .., and cannot be one of the reserved names {string.Join(", ", ReservedNames)}");
    }

    private static IRuleBuilderOptions<T, string> ArchiveSafeNameValidation<T>(this IRuleBuilder<T, string> rule, string entityName)
    {
        return rule
            .NotEmpty()
            .MaximumLength(90)
            .Must(x => string.IsNullOrEmpty(x) || IsValidName(x))
            .WithMessage($"{entityName} names can only include letters, numbers, -, _, and ., cannot be . or .., and cannot be one of the reserved names {string.Join(", ", ReservedNames)}");
    }

    private static bool IsValidName(string name)
    {
        return name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_' || c == '.') &&
            name != "." &&
            name != ".." &&
            !IsReservedName(name);
    }

    public static bool IsReservedName(string name)
    {
        return ReservedNames.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
    }
}
