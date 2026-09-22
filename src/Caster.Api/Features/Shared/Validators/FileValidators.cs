// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.IO;
using System.Linq;
using FluentValidation;

namespace Caster.Api.Features.Shared.Validators;

public static class FileValidationRules
{
    /// <summary>
    /// Path.GetInvalidFileNameChars() only reports the characters that are invalid on the running
    /// platform, which on Linux is just the null character and /, so the separator used by other
    /// platforms is added rather than letting it through.
    /// </summary>
    private static readonly char[] InvalidFileNameChars = [.. Path.GetInvalidFileNameChars(), '/', '\\'];

    /// <summary>
    /// File names are written directly into a Workspace's working directory on disk, so they must
    /// not be able to reference anything outside of it. Anything that cannot escape that directory
    /// is allowed, so that names accepted before this rule existed keep working.
    /// </summary>
    public static IRuleBuilderOptions<T, string> FileNameValidation<T>(this IRuleBuilder<T, string> rule)
    {
        return rule
            .NotEmpty()
            .MaximumLength(255)
            .Must(x => string.IsNullOrEmpty(x) ||
                (x != "." && x != ".." &&
                 x.IndexOfAny(InvalidFileNameChars) < 0 &&
                 !x.Any(char.IsControl) &&
                 x.Trim() == x))
            .WithMessage("File names cannot be . or .., cannot contain path separators or control characters, and cannot begin or end with whitespace");
    }
}
