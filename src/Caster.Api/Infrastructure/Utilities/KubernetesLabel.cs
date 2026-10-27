// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Text.RegularExpressions;

namespace Caster.Api.Infrastructure.Utilities;

public static class KubernetesLabel
{
    private const int MaxLength = 63;

    /// <summary>
    /// Converts a value into a valid Kubernetes label value: at most 63 characters
    /// from [A-Za-z0-9._-], beginning and ending with an alphanumeric character.
    /// Invalid characters are replaced with '_'.
    /// </summary>
    public static string ToValue(string value)
    {
        var label = Regex.Replace(value ?? string.Empty, "[^A-Za-z0-9._-]", "_");

        if (label.Length > MaxLength)
        {
            label = label[..MaxLength];
        }

        return label.Trim('-', '_', '.');
    }
}
