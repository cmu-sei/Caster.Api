// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Caster.Api.Domain.Services.EnvironmentSets;

/// <summary>
/// Replaces secret values in Terraform output before it is saved or streamed. Catches
/// accidental echoes (a provider error quoting a credential, debug logging); it cannot stop
/// a configuration that sets out to reveal a value, encoded or split.
/// </summary>
public class OutputMasker
{
    public const string Mask = "***";

    public static readonly OutputMasker None = new([]);

    private readonly string[] _values;

    private readonly int _minimumLength;

    /// <param name="secretValues">The values to mask.</param>
    /// <param name="minimumLength">
    /// Values, and lines of multi-line values, shorter than this are not masked: they would hit
    /// ordinary text. A run is refused a secret this short, so only lines of one are skipped.
    /// </param>
    public OutputMasker(IEnumerable<string> secretValues, int minimumLength = EnvironmentSetRules.DefaultMinimumMaskedLength)
    {
        _minimumLength = Math.Max(1, minimumLength);
        var values = new HashSet<string>(StringComparer.Ordinal);

        foreach (var value in secretValues ?? [])
        {
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            Add(values, value);

            // Output is handled a line at a time, so a multi-line value (a PEM key) is also
            // masked line by line.
            foreach (var line in value.Split('\n'))
            {
                Add(values, line.TrimEnd('\r'));
            }
        }

        // Longest first, so a value that contains another is masked whole.
        _values = values.OrderByDescending(x => x.Length).ToArray();
    }

    public bool IsEmpty => _values.Length == 0;

    /// <summary>This masker's values and <paramref name="more"/>, with the same minimum length.</summary>
    public OutputMasker With(IEnumerable<string> more) => new(_values.Concat(more ?? []), _minimumLength);

    public string Apply(string text)
    {
        if (string.IsNullOrEmpty(text) || _values.Length == 0)
        {
            return text;
        }

        foreach (var value in _values)
        {
            text = text.Replace(value, Mask, StringComparison.Ordinal);
        }

        return text;
    }

    private void Add(HashSet<string> values, string value)
    {
        if (value.Trim().Length >= _minimumLength)
        {
            values.Add(value);
        }
    }
}
