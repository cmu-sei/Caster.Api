// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Caster.Api.Domain.Models;

namespace Caster.Api.Domain.Services.EnvironmentSets;

/// <summary>The naming rules for Environment Set entries, shared by the API validators and the run-time builder.</summary>
public static partial class EnvironmentSetRules
{
    /// <summary>Secret values shorter than this are not masked in run output: too likely to hit ordinary text.</summary>
    public const int MinimumMaskedLength = 8;

    private const string TerraformPrefix = "TF_";
    private const string TerraformTokenPrefix = "TF_TOKEN_";

    private static readonly string[] ReservedEnvironmentVariables = ["HOME", "PATH"];

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex EnvironmentVariableNameRegex();

    // Terraform identifiers: letters, digits, underscores and hyphens, not starting with a digit.
    [GeneratedRegex("^[A-Za-z_-][A-Za-z0-9_-]*$")]
    private static partial Regex TerraformVariableNameRegex();

    public static bool IsValidEnvironmentVariableName(string name) =>
        !string.IsNullOrEmpty(name) && EnvironmentVariableNameRegex().IsMatch(name);

    /// <summary>
    /// Null if <paramref name="targetName"/> is allowed for <paramref name="kind"/>,
    /// otherwise why it is not.
    /// </summary>
    public static string GetTargetNameError(EnvironmentSetEntryKind kind, string targetName)
    {
        if (string.IsNullOrEmpty(targetName))
        {
            return "A target name is required.";
        }

        if (kind == EnvironmentSetEntryKind.TerraformVariable)
        {
            if (!TerraformVariableNameRegex().IsMatch(targetName))
            {
                return "Terraform variable names may only contain letters, digits, underscores and hyphens, and cannot start with a digit.";
            }

            return null;
        }

        if (!EnvironmentVariableNameRegex().IsMatch(targetName))
        {
            return "Environment variable names may only contain letters, digits and underscores, and cannot start with a digit.";
        }

        if (targetName.StartsWith(EnvironmentSetEntry.TerraformVariablePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return $"Use the Terraform variable kind for {EnvironmentSetEntry.TerraformVariablePrefix} variables, with the name that follows the prefix.";
        }

        if (targetName.StartsWith(TerraformPrefix, StringComparison.OrdinalIgnoreCase) &&
            !targetName.StartsWith(TerraformTokenPrefix, StringComparison.Ordinal))
        {
            return $"Environment variables starting with {TerraformPrefix} control Terraform itself and cannot be set. {TerraformTokenPrefix}* (private registry credentials) is allowed.";
        }

        if (ReservedEnvironmentVariables.Contains(targetName, StringComparer.OrdinalIgnoreCase))
        {
            return $"{string.Join(" and ", ReservedEnvironmentVariables)} cannot be set.";
        }

        return null;
    }

    /// <summary>
    /// The effective names (<see cref="EnvironmentSetEntry.EffectiveName"/>) that appear in
    /// more than one of <paramref name="entries"/>.
    /// </summary>
    public static IEnumerable<string> GetDuplicateNames(IEnumerable<EnvironmentSetEntry> entries) =>
        entries
            .GroupBy(x => x.EffectiveName, StringComparer.Ordinal)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key);

    /// <summary>The name shown for an entry: the Terraform variable name or the environment variable name.</summary>
    public static string Describe(EnvironmentSetEntry entry) =>
        entry.Kind == EnvironmentSetEntryKind.TerraformVariable
            ? $"Terraform variable {entry.TargetName}"
            : $"environment variable {entry.TargetName}";
}
