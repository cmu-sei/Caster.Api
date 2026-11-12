// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Caster.Api.Domain.Services.EnvironmentSets;

/// <summary>The API process's own environment variables: where inherited variables and secrets come from.</summary>
public interface IApiEnvironment
{
    IReadOnlyDictionary<string, string> GetVariables();

    /// <summary>The value of <paramref name="name"/>, or null if it is not set.</summary>
    string GetVariable(string name);
}

public class ApiEnvironment : IApiEnvironment
{
    public IReadOnlyDictionary<string, string> GetVariables() =>
        Environment.GetEnvironmentVariables()
            .Cast<DictionaryEntry>()
            .ToDictionary(
                e => e.Key?.ToString() ?? string.Empty,
                e => e.Value?.ToString() ?? string.Empty,
                StringComparer.Ordinal);

    public string GetVariable(string name) =>
        string.IsNullOrEmpty(name) ? null : Environment.GetEnvironmentVariable(name);
}
