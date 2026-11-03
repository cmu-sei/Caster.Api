// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;

namespace Caster.Api.Domain.Services.EnvironmentSets;

/// <summary>
/// Where Environment Set secrets come from: API environment variables whose names match
/// <c>Terraform:Secrets:Patterns</c>. Every API replica is assumed to have the same environment.
/// </summary>
public interface ISecretSource
{
    /// <summary>True if <paramref name="name"/> matches a secret pattern, whether or not it is set.</summary>
    bool IsAllowed(string name);

    /// <summary>True if <paramref name="name"/> matches a secret pattern and is set.</summary>
    bool IsAvailable(string name);

    /// <summary>The value of <paramref name="name"/> if it matches a secret pattern and is set; otherwise null.</summary>
    string GetValue(string name);

    /// <summary>The names (never the values) of every API environment variable matching a secret pattern.</summary>
    string[] GetAvailableNames();
}

public class SecretSource(IApiEnvironment apiEnvironment, IRegexService regexService) : ISecretSource
{
    public bool IsAllowed(string name) =>
        EnvironmentSetRules.IsValidEnvironmentVariableName(name) && regexService.IsSecretName(name);

    public bool IsAvailable(string name) => GetValue(name) is not null;

    public string GetValue(string name) =>
        IsAllowed(name) ? apiEnvironment.GetVariable(name) : null;

    public string[] GetAvailableNames() =>
        apiEnvironment.GetVariables().Keys
            .Where(IsAllowed)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
}
