// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Caster.Api.Domain.Models;

/// <summary>
/// A value still to be filled in, such as REPLACE_WITH_DC_TEMPLATE: what an AI draft puts
/// where it does not know a template, node, zone or other name from the range. A Design
/// may be saved with them, but not enabled, since Terraform would be handed them as real.
/// </summary>
public static class Placeholders
{
    public const string Prefix = "REPLACE_WITH_";

    private static readonly Regex Pattern = new($@"\b{Prefix}[A-Za-z0-9_]*");

    public static bool IsPlaceholder(string value) => value != null && Pattern.IsMatch(value);

    /// <summary>
    /// The placeholder for a variable left without a value: REPLACE_WITH_TEMPLATE_NAME.
    /// </summary>
    public static string For(string variableName) =>
        Prefix + Regex.Replace(variableName ?? "", "[^A-Za-z0-9]+", "_").ToUpperInvariant();

    /// <summary>
    /// The names of the variables whose values are, or contain, a placeholder.
    /// </summary>
    public static string[] In(IEnumerable<ModuleValue> values) =>
        (values ?? [])
            .Where(v => IsPlaceholder(v.Value))
            .Select(v => v.Name)
            .ToArray();
}
