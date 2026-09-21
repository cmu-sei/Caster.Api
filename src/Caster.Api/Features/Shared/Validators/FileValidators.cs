// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using FluentValidation;

namespace Caster.Api.Features.Shared.Validators;

public static class FileValidationRules
{
    /// <summary>
    /// File names are written directly into a Workspace's working directory on disk, so they must
    /// not be able to reference anything outside of it.
    /// </summary>
    public static IRuleBuilderOptions<T, string> FileNameValidation<T>(this IRuleBuilder<T, string> rule)
    {
        return rule
            .NotEmpty()
            .MaximumLength(255)
            .Must(x => string.IsNullOrEmpty(x) ||
                (x != "." && x != ".." && x.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.')))
            .WithMessage("File names can only include letters, numbers, -, _, and .");
    }
}
