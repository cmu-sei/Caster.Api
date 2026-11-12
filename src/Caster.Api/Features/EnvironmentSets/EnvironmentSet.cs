// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.EnvironmentSets;

/// <summary>
/// An Environment Set as admins see it (ViewEnvironmentSets): plain values and secret source
/// names, never secret values.
/// </summary>
public class EnvironmentSet
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }

    /// <summary>False if any entry can't be used by a run: see <see cref="EnvironmentSetEntry.Unavailable"/>.</summary>
    public bool IsComplete { get; set; }

    public EnvironmentSetEntry[] Entries { get; set; } = [];

    /// <summary>The projects this set is assigned to.</summary>
    public EnvironmentSetProject[] Projects { get; set; } = [];
}

public class EnvironmentSetEntry
{
    public Guid Id { get; set; }
    public Guid EnvironmentSetId { get; set; }
    public EnvironmentSetEntryKind Kind { get; set; }

    /// <summary>The name inside the run. For a Terraform variable, without <c>TF_VAR_</c>.</summary>
    public string TargetName { get; set; }

    public EnvironmentSetValueSource ValueSource { get; set; }

    /// <summary>For a secret: the API environment variable its value is read from.</summary>
    public string SecretName { get; set; }

    /// <summary>For a plain entry: the stored value.</summary>
    public string Value { get; set; }

    /// <summary>For a connection entry: the infrastructure connection, by name.</summary>
    public string ConnectionName { get; set; }

    /// <summary>For a connection entry: what it takes from the connection.</summary>
    public ConnectionField? ConnectionField { get; set; }

    /// <summary>Whether the value is a secret: a secret entry, or a connection credential.</summary>
    public bool IsSecret { get; set; }

    /// <summary>Whether a run can use the entry. Always true for a plain entry.</summary>
    public bool IsAvailable { get; set; }

    /// <summary>Why a run can't use the entry, when it can't. Never a value.</summary>
    public string Unavailable { get; set; }
}

public class EnvironmentSetProject
{
    public Guid ProjectId { get; set; }
    public string Name { get; set; }
}

/// <summary>
/// An Environment Set as the members of a project it is assigned to see it (ViewProject):
/// target names and plain values, and whether each secret is available. Never secret source
/// names or values.
/// </summary>
public class ProjectEnvironmentSet
{
    public Guid EnvironmentSetId { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public bool IsComplete { get; set; }
    public ProjectEnvironmentSetEntry[] Entries { get; set; } = [];
}

public class ProjectEnvironmentSetEntry
{
    public Guid Id { get; set; }
    public EnvironmentSetEntryKind Kind { get; set; }
    public string TargetName { get; set; }
    public EnvironmentSetValueSource ValueSource { get; set; }

    /// <summary>For a plain entry: the stored value. Null for a secret or a connection entry.</summary>
    public string Value { get; set; }

    /// <summary>For a connection entry that isn't a credential: the connection, by name, and the field.</summary>
    public string ConnectionName { get; set; }

    public ConnectionField? ConnectionField { get; set; }

    /// <summary>Whether the value is a secret: a secret entry, or a connection credential.</summary>
    public bool IsSecret { get; set; }

    public bool IsAvailable { get; set; }
}

public enum TerraformRunMode
{
    Process,
    Kubernetes
}

/// <summary>What admins need to build Environment Sets. Names only, never values.</summary>
public class EnvironmentSecrets
{
    /// <summary>API environment variables matching the secret patterns.</summary>
    public string[] Available { get; set; } = [];

    /// <summary>Source names used by Environment Set entries that are not available.</summary>
    public string[] Missing { get; set; } = [];

    /// <summary>The secret patterns (<c>Terraform:Secrets:Patterns</c>).</summary>
    public string[] Patterns { get; set; } = [];

    /// <summary>
    /// How runs execute: fixed when the API starts (<c>Terraform:KubernetesJobs:Enabled</c>). In Kubernetes job mode, set values are plain env vars in the Job spec,
    /// and Terraform variable values also reach pod logs.
    /// </summary>
    public TerraformRunMode RunMode { get; set; }

    /// <summary>Whether every non-secret API environment variable reaches every run.</summary>
    public bool InheritAll { get; set; }

    /// <summary>
    /// The shortest secret value a run may get (<c>Terraform:Secrets:MinimumMaskedLength</c>).
    /// A run whose secret is shorter fails before Terraform starts.
    /// </summary>
    public int MinimumMaskedLength { get; set; }

    /// <summary>Available secret names whose values are shorter than <see cref="MinimumMaskedLength"/>.</summary>
    public string[] TooShort { get; set; } = [];
}
