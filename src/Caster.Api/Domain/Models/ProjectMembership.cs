// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Caster.Api.Domain.Models;

public class ProjectMembership : IEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }
    public virtual Project Project { get; set; }

    public Guid? UserId { get; set; }
    public virtual User User { get; set; }

    public Guid? GroupId { get; set; }
    public virtual Group Group { get; set; }

    public Guid RoleId { get; set; } = ProjectRoleDefaults.ProjectMemberRoleId;
    public ProjectRole Role { get; set; }


    public ProjectMembership() { }

    public ProjectMembership(Guid projectId, Guid? userId, Guid? groupId)
    {
        ProjectId = projectId;
        UserId = userId;
        GroupId = groupId;
    }

    public class ProjectMembershipConfiguration : IEntityTypeConfiguration<ProjectMembership>
    {
        public void Configure(EntityTypeBuilder<ProjectMembership> builder)
        {
            builder.HasIndex(e => new { e.ProjectId, e.UserId, e.GroupId }).IsUnique();

            builder.Property(x => x.RoleId).HasDefaultValue(ProjectRoleDefaults.ProjectMemberRoleId);

            builder
                .HasOne(x => x.Project)
                .WithMany(x => x.Memberships)
                .HasForeignKey(x => x.ProjectId);

            builder
                .HasOne(x => x.User)
                .WithMany(x => x.ProjectMemberships)
                .HasForeignKey(x => x.UserId)
                .HasPrincipalKey(x => x.Id);

            builder
                .HasOne(x => x.Group)
                .WithMany(x => x.ProjectMemberships)
                .HasForeignKey(x => x.GroupId)
                .HasPrincipalKey(x => x.Id);
        }
    }
}
