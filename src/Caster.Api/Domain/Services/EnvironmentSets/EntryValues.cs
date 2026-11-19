// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.Inventory;
using Caster.Api.Infrastructure.Options;

namespace Caster.Api.Domain.Services.EnvironmentSets;

/// <summary>
/// An entry's value as a run would get it, or why a run can't. <see cref="Unavailable"/> is
/// written for run output and project members: it names target names, sets and connections,
/// never a secret source name or a value.
/// </summary>
public record EntryValue(string Value, bool IsSecret, string Unavailable)
{
    public bool IsAvailable => Unavailable is null;
}

/// <summary>Resolves Environment Set entries, the same way for runs and for the views.</summary>
public interface IEntryValues
{
    /// <summary>The shortest secret value a run may get (<c>Terraform:Secrets:MinimumMaskedLength</c>).</summary>
    int MinimumMaskedLength { get; }

    EntryValue Resolve(EnvironmentSetEntry entry);

    /// <summary>
    /// Whether the entry's value is a secret, from its source alone: a Secret entry, or a
    /// credential field of a connection.
    /// </summary>
    bool IsSecret(EnvironmentSetEntry entry);
}

public class EntryValues(
    ISecretSource secretSource,
    IInfrastructureConnectionCatalog connections,
    TerraformOptions options) : IEntryValues
{
    public int MinimumMaskedLength =>
        options.Secrets?.MinimumMaskedLength is > 0 and var minimum ? minimum : EnvironmentSetRules.DefaultMinimumMaskedLength;

    public bool IsSecret(EnvironmentSetEntry entry) => entry.ValueSource switch
    {
        EnvironmentSetValueSource.Secret => true,
        EnvironmentSetValueSource.Connection => entry.ConnectionField is { } field && ConnectionFields.IsSecret(field),
        _ => false
    };

    public EntryValue Resolve(EnvironmentSetEntry entry)
    {
        var value = entry.ValueSource switch
        {
            EnvironmentSetValueSource.Plain => new EntryValue(entry.Value ?? string.Empty, false, null),
            EnvironmentSetValueSource.Secret => FromSecret(entry),
            EnvironmentSetValueSource.Connection => FromConnection(entry),
            _ => new EntryValue(null, false, "its value source is not known.")
        };

        if (!value.IsAvailable || !value.IsSecret)
            return value;

        if (entry.Kind == EnvironmentSetEntryKind.TerraformVariable)
            return value with { Value = null, Unavailable = "it is a secret, and a secret can't be a Terraform variable: Terraform saves variables in plans and state." };

        if (value.Value.Length < MinimumMaskedLength)
            return value with { Value = null, Unavailable = $"its secret is shorter than {MinimumMaskedLength} characters, too short to mask in run output." };

        return value;
    }

    private EntryValue FromSecret(EnvironmentSetEntry entry)
    {
        if (!secretSource.IsAllowed(entry.SecretName))
            return new EntryValue(null, true, "its secret no longer matches the secret patterns.");

        var value = secretSource.GetValue(entry.SecretName);

        return value is null
            ? new EntryValue(null, true, "its secret is not available.")
            : new EntryValue(value, true, null);
    }

    private EntryValue FromConnection(EnvironmentSetEntry entry)
    {
        var secret = IsSecret(entry);

        if (entry.ConnectionField is not { } field)
            return new EntryValue(null, secret, "it names no connection field.");

        var connection = connections.Find(entry.ConnectionName);

        if (connection is null)
            return new EntryValue(null, secret, $"the infrastructure connection \"{entry.ConnectionName}\" is not configured.");

        var resolved = ConnectionFields.Resolve(connection.Options, field);

        return resolved.IsAvailable
            ? new EntryValue(resolved.Value, secret, null)
            : new EntryValue(null, secret, $"the {field} of connection \"{connection.Name}\" is not available: {resolved.Unavailable}");
    }
}
