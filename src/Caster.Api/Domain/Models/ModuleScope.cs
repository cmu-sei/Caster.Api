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
/// A named list of module sources, or folders of them, that admins assign to projects and project
/// groups. A project with no scope assigned, directly or through its group, may use every Module;
/// once it has one, only the Modules whose Path matches an entry of its scopes. Never part of a
/// project export or import.
/// </summary>
public class ModuleScope : IEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public string Name { get; set; }

    public string Description { get; set; }

    /// <summary>
    /// What the scope allows: a module source's name (<c>afp</c>) or a folder of one
    /// (<c>afp/exercise-x</c>). Each allows the Modules at exactly that Path and those below it.
    /// Stored normalized (see <c>ModuleScopePaths</c>), never empty.
    /// </summary>
    public List<string> Paths { get; set; } = new List<string>();

    public virtual ICollection<ProjectModuleScope> Projects { get; set; } = new List<ProjectModuleScope>();

    public virtual ICollection<ProjectGroupModuleScope> ProjectGroups { get; set; } = new List<ProjectGroupModuleScope>();
}

/// <summary>The assignment of a <see cref="ModuleScope"/> to a <see cref="Project"/>.</summary>
public class ProjectModuleScope : IEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }
    public virtual Project Project { get; set; }

    public Guid ModuleScopeId { get; set; }
    public virtual ModuleScope ModuleScope { get; set; }
}

/// <summary>The assignment of a <see cref="ModuleScope"/> to every project of a group.</summary>
public class ProjectGroupModuleScope : IEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public Guid ProjectGroupId { get; set; }
    public virtual ProjectGroup ProjectGroup { get; set; }

    public Guid ModuleScopeId { get; set; }
    public virtual ModuleScope ModuleScope { get; set; }
}

public class ModuleScopeConfiguration : IEntityTypeConfiguration<ModuleScope>
{
    public void Configure(EntityTypeBuilder<ModuleScope> builder)
    {
        builder.HasIndex(x => x.Name).IsUnique();
        builder.Property(x => x.Name).IsRequired();
        builder.Property(x => x.Paths).IsRequired();
    }
}

public class ProjectModuleScopeConfiguration : IEntityTypeConfiguration<ProjectModuleScope>
{
    public void Configure(EntityTypeBuilder<ProjectModuleScope> builder)
    {
        builder.HasIndex(x => new { x.ProjectId, x.ModuleScopeId }).IsUnique();
        builder.HasIndex(x => x.ModuleScopeId);

        builder
            .HasOne(x => x.Project)
            .WithMany(x => x.ModuleScopes)
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(x => x.ModuleScope)
            .WithMany(x => x.Projects)
            .HasForeignKey(x => x.ModuleScopeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProjectGroupModuleScopeConfiguration : IEntityTypeConfiguration<ProjectGroupModuleScope>
{
    public void Configure(EntityTypeBuilder<ProjectGroupModuleScope> builder)
    {
        builder.HasIndex(x => new { x.ProjectGroupId, x.ModuleScopeId }).IsUnique();
        builder.HasIndex(x => x.ModuleScopeId);

        builder
            .HasOne(x => x.ProjectGroup)
            .WithMany(x => x.ModuleScopes)
            .HasForeignKey(x => x.ProjectGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(x => x.ModuleScope)
            .WithMany(x => x.ProjectGroups)
            .HasForeignKey(x => x.ModuleScopeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
