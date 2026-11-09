// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Caster.Api.Domain.Models;

/// <summary>
/// A group of projects that share memberships, Environment Sets, inventory connections, a
/// partition and hosts. A project is in at most one group, and groups don't nest. What a group
/// carries adds to what each of its projects has of its own.
/// </summary>
public class ProjectGroup : IEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public string Name { get; set; }

    public string Description { get; set; }

    /// <summary>The partition the group's projects acquire VLANs from when they have none of their own.</summary>
    public Guid? PartitionId { get; set; }
    public virtual Partition Partition { get; set; }

    public virtual ICollection<Project> Projects { get; set; } = new List<Project>();
    public virtual ICollection<ProjectGroupMembership> Memberships { get; set; } = new List<ProjectGroupMembership>();
    public virtual ICollection<ProjectGroupEnvironmentSet> EnvironmentSets { get; set; } = new List<ProjectGroupEnvironmentSet>();
    public virtual ICollection<ProjectGroupInventoryConnection> InventoryConnections { get; set; } = new List<ProjectGroupInventoryConnection>();
    public virtual ICollection<Host> Hosts { get; set; } = new List<Host>();
}

/// <summary>
/// A user or a user group with a project role in every project of a project group. Mirrors
/// <see cref="ProjectMembership"/>, in a table of its own so that one's unique index is unchanged.
/// </summary>
public class ProjectGroupMembership : IEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public Guid ProjectGroupId { get; set; }
    public virtual ProjectGroup ProjectGroup { get; set; }

    public Guid? UserId { get; set; }
    public virtual User User { get; set; }

    public Guid? GroupId { get; set; }
    public virtual Group Group { get; set; }

    public Guid RoleId { get; set; } = ProjectRoleDefaults.ProjectMemberRoleId;
    public virtual ProjectRole Role { get; set; }
}

/// <summary>The assignment of an <see cref="EnvironmentSet"/> to every project of a group.</summary>
public class ProjectGroupEnvironmentSet : IEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public Guid ProjectGroupId { get; set; }
    public virtual ProjectGroup ProjectGroup { get; set; }

    public Guid EnvironmentSetId { get; set; }
    public virtual EnvironmentSet EnvironmentSet { get; set; }
}

/// <summary>An infrastructure connection whose inventory every project of a group sees.</summary>
public class ProjectGroupInventoryConnection : IEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public Guid ProjectGroupId { get; set; }
    public virtual ProjectGroup ProjectGroup { get; set; }

    /// <summary>The connection's Name, as configuration spelled it when it was assigned.</summary>
    public string ConnectionName { get; set; }

    /// <summary>See <see cref="ProjectInventoryConnection.NormalizedConnectionName"/>.</summary>
    public string NormalizedConnectionName { get; set; }
}

/// <summary>What a membership of a project group allows on the group itself.</summary>
public enum ProjectGroupPermission
{
    /// <summary>See the group: every member has it.</summary>
    ViewProjectGroup,

    /// <summary>Add, edit and remove the group's memberships.</summary>
    ManageMembership,

    /// <summary>Add projects to the group and remove them.</summary>
    ManageProjects
}

/// <summary>Whether a project role makes its holder a manager of a project group it's held in.</summary>
public static class ProjectGroupManagers
{
    /// <summary>
    /// A role with AllPermissions (the seeded Manager role) or ManageProject, which is what controls
    /// a project's own memberships.
    /// </summary>
    public static bool IsManagerRole(ProjectRole role) =>
        role is not null && (role.AllPermissions || (role.Permissions?.Contains(ProjectPermission.ManageProject) ?? false));
}

public class ProjectGroupConfiguration : IEntityTypeConfiguration<ProjectGroup>
{
    public void Configure(EntityTypeBuilder<ProjectGroup> builder)
    {
        builder.Property(x => x.Name).IsRequired();
        builder.HasIndex(x => x.Name).IsUnique();

        builder
            .HasOne(x => x.Partition)
            .WithMany()
            .HasForeignKey(x => x.PartitionId)
            .OnDelete(DeleteBehavior.SetNull);

        // Deleting a group leaves its projects and hosts, ungrouped.
        builder
            .HasMany(x => x.Projects)
            .WithOne(x => x.ProjectGroup)
            .HasForeignKey(x => x.ProjectGroupId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasMany(x => x.Hosts)
            .WithOne(x => x.ProjectGroup)
            .HasForeignKey(x => x.ProjectGroupId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class ProjectGroupMembershipConfiguration : IEntityTypeConfiguration<ProjectGroupMembership>
{
    public void Configure(EntityTypeBuilder<ProjectGroupMembership> builder)
    {
        builder.HasIndex(x => new { x.ProjectGroupId, x.UserId, x.GroupId }).IsUnique();
        builder.Property(x => x.RoleId).HasDefaultValue(ProjectRoleDefaults.ProjectMemberRoleId);

        builder
            .HasOne(x => x.ProjectGroup)
            .WithMany(x => x.Memberships)
            .HasForeignKey(x => x.ProjectGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId);

        builder.ToTable(x => x.HasCheckConstraint(
            "CK_project_group_memberships_user_or_group",
            "(user_id IS NULL) <> (group_id IS NULL)"));
    }
}

public class ProjectGroupEnvironmentSetConfiguration : IEntityTypeConfiguration<ProjectGroupEnvironmentSet>
{
    public void Configure(EntityTypeBuilder<ProjectGroupEnvironmentSet> builder)
    {
        builder.HasIndex(x => new { x.ProjectGroupId, x.EnvironmentSetId }).IsUnique();
        builder.HasIndex(x => x.EnvironmentSetId);

        builder
            .HasOne(x => x.ProjectGroup)
            .WithMany(x => x.EnvironmentSets)
            .HasForeignKey(x => x.ProjectGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(x => x.EnvironmentSet)
            .WithMany(x => x.ProjectGroups)
            .HasForeignKey(x => x.EnvironmentSetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProjectGroupInventoryConnectionConfiguration : IEntityTypeConfiguration<ProjectGroupInventoryConnection>
{
    public void Configure(EntityTypeBuilder<ProjectGroupInventoryConnection> builder)
    {
        builder.Property(x => x.ConnectionName).IsRequired();
        builder.Property(x => x.NormalizedConnectionName).IsRequired();
        builder.HasIndex(x => new { x.ProjectGroupId, x.NormalizedConnectionName }).IsUnique();
        builder.HasIndex(x => x.NormalizedConnectionName);

        builder
            .HasOne(x => x.ProjectGroup)
            .WithMany(x => x.InventoryConnections)
            .HasForeignKey(x => x.ProjectGroupId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
