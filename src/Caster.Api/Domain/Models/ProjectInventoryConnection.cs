// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Caster.Api.Domain.Models;

/// <summary>
/// An infrastructure connection whose inventory a project's members can see. Separate from
/// Environment Sets, which only hand out credentials. Connections live in configuration, so the
/// name is checked when assigned; one that later leaves configuration stays, shown as missing.
/// Never part of a project export.
/// </summary>
public class ProjectInventoryConnection : IEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }
    public virtual Project Project { get; set; }

    /// <summary>The connection's Name, as configuration spelled it when it was assigned.</summary>
    public string ConnectionName { get; set; }

    /// <summary>
    /// <see cref="ConnectionName"/> trimmed and lower-cased, the way configuration compares
    /// names, so one connection can't be assigned twice under different spellings.
    /// </summary>
    public string NormalizedConnectionName { get; set; }

    public static string Normalize(string name) => name?.Trim().ToLowerInvariant();
}

public class ProjectInventoryConnectionConfiguration : IEntityTypeConfiguration<ProjectInventoryConnection>
{
    public void Configure(EntityTypeBuilder<ProjectInventoryConnection> builder)
    {
        builder.Property(x => x.ConnectionName).IsRequired();
        builder.Property(x => x.NormalizedConnectionName).IsRequired();
        builder.HasIndex(x => new { x.ProjectId, x.NormalizedConnectionName }).IsUnique();
        builder.HasIndex(x => x.NormalizedConnectionName);

        builder
            .HasOne(x => x.Project)
            .WithMany(x => x.InventoryConnections)
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
