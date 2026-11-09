// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Caster.Api.Domain.Models;

/// <summary>
/// A named group of environment variables and Terraform variables that admins assign to
/// projects. Only the sets assigned to a project reach that project's Terraform runs.
/// Never part of a project export or import.
/// </summary>
public class EnvironmentSet : IEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public string Name { get; set; }

    public string Description { get; set; }

    public virtual ICollection<EnvironmentSetEntry> Entries { get; set; } = new List<EnvironmentSetEntry>();

    public virtual ICollection<ProjectEnvironmentSet> Projects { get; set; } = new List<ProjectEnvironmentSet>();

    public virtual ICollection<ProjectGroupEnvironmentSet> ProjectGroups { get; set; } = new List<ProjectGroupEnvironmentSet>();
}

public enum EnvironmentSetEntryKind
{
    /// <summary>Injected as an environment variable named <see cref="EnvironmentSetEntry.TargetName"/>.</summary>
    EnvironmentVariable,

    /// <summary>Injected as <c>TF_VAR_</c> + <see cref="EnvironmentSetEntry.TargetName"/>. HCL values allowed.</summary>
    TerraformVariable
}

public enum EnvironmentSetValueSource
{
    /// <summary>The value is stored in Caster and is not secret.</summary>
    Plain,

    /// <summary>
    /// The value is read at run time from the API environment variable named
    /// <see cref="EnvironmentSetEntry.SecretName"/>, which must match a secret pattern.
    /// </summary>
    Secret,

    /// <summary>
    /// The value is read at run time from the infrastructure connection named
    /// <see cref="EnvironmentSetEntry.ConnectionName"/>: its <see cref="EnvironmentSetEntry.ConnectionField"/>.
    /// </summary>
    Connection
}

/// <summary>
/// What a <see cref="EnvironmentSetValueSource.Connection"/> entry takes from its connection.
/// A fixed list, worked out from Caster's connection settings, not from any Terraform provider.
/// </summary>
public enum ConnectionField
{
    /// <summary>The connection's Url, as configured.</summary>
    Url,

    /// <summary>The host, and the port when the Url names one, without scheme or path.</summary>
    Host,

    /// <summary>Proxmox: the Url with /api2/json, which the Telmate provider takes.</summary>
    ApiUrl,

    /// <summary>"true" or "false", from InsecureSkipVerify.</summary>
    Insecure,

    Username,

    /// <summary>Secret.</summary>
    Password,

    /// <summary>Proxmox, secret: the whole user@realm!id=secret token.</summary>
    ApiToken,

    /// <summary>Proxmox, secret: the token before its first '=', user@realm!id.</summary>
    ApiTokenId,

    /// <summary>Proxmox, secret: the token after its first '='.</summary>
    ApiTokenSecret,

    /// <summary>The datastore or storage of the connection's only IsoLocations entry.</summary>
    IsoStorage,

    /// <summary>vSphere: the folder of the connection's only IsoLocations entry, empty for none.</summary>
    IsoFolder,

    /// <summary>vSphere: the connection's only Datacenters entry.</summary>
    Datacenter
}

public class EnvironmentSetEntry : IEntity
{
    public const string TerraformVariablePrefix = "TF_VAR_";

    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public Guid EnvironmentSetId { get; set; }
    public virtual EnvironmentSet EnvironmentSet { get; set; }

    public EnvironmentSetEntryKind Kind { get; set; }

    /// <summary>The name inside the run: the variable name, or the Terraform variable name without <c>TF_VAR_</c>.</summary>
    public string TargetName { get; set; }

    public EnvironmentSetValueSource ValueSource { get; set; }

    /// <summary>For <see cref="EnvironmentSetValueSource.Secret"/>: the API environment variable to read.</summary>
    public string SecretName { get; set; }

    /// <summary>For <see cref="EnvironmentSetValueSource.Plain"/>: the stored value.</summary>
    public string Value { get; set; }

    /// <summary>
    /// For <see cref="EnvironmentSetValueSource.Connection"/>: the infrastructure connection's Name,
    /// as spelled in configuration when the entry was saved. Matched ignoring case.
    /// </summary>
    public string ConnectionName { get; set; }

    /// <summary>For <see cref="EnvironmentSetValueSource.Connection"/>: what the entry takes from the connection.</summary>
    public ConnectionField? ConnectionField { get; set; }

    /// <summary>The environment variable name this entry sets in the run.</summary>
    [NotMapped]
    public string EffectiveName => GetEffectiveName(Kind, TargetName);

    public static string GetEffectiveName(EnvironmentSetEntryKind kind, string targetName) =>
        kind == EnvironmentSetEntryKind.TerraformVariable ? $"{TerraformVariablePrefix}{targetName}" : targetName;
}

/// <summary>The assignment of an <see cref="EnvironmentSet"/> to a <see cref="Project"/>.</summary>
public class ProjectEnvironmentSet : IEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }
    public virtual Project Project { get; set; }

    public Guid EnvironmentSetId { get; set; }
    public virtual EnvironmentSet EnvironmentSet { get; set; }
}

public class EnvironmentSetConfiguration : IEntityTypeConfiguration<EnvironmentSet>
{
    public void Configure(EntityTypeBuilder<EnvironmentSet> builder)
    {
        builder.HasIndex(x => x.Name).IsUnique();
        builder.Property(x => x.Name).IsRequired();
    }
}

public class EnvironmentSetEntryConfiguration : IEntityTypeConfiguration<EnvironmentSetEntry>
{
    public void Configure(EntityTypeBuilder<EnvironmentSetEntry> builder)
    {
        builder.Property(x => x.TargetName).IsRequired();

        builder
            .HasOne(x => x.EnvironmentSet)
            .WithMany(x => x.Entries)
            .HasForeignKey(x => x.EnvironmentSetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.EnvironmentSetId, x.Kind, x.TargetName }).IsUnique();
    }
}

public class ProjectEnvironmentSetConfiguration : IEntityTypeConfiguration<ProjectEnvironmentSet>
{
    public void Configure(EntityTypeBuilder<ProjectEnvironmentSet> builder)
    {
        builder.HasIndex(x => new { x.ProjectId, x.EnvironmentSetId }).IsUnique();
        builder.HasIndex(x => x.EnvironmentSetId);

        builder
            .HasOne(x => x.Project)
            .WithMany(x => x.EnvironmentSets)
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(x => x.EnvironmentSet)
            .WithMany(x => x.Projects)
            .HasForeignKey(x => x.EnvironmentSetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
