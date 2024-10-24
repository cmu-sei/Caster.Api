// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Caster.Api.Domain.Models;

public class ProjectMembership
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }
    public virtual Project Project { get; set; }

    public Guid UserId { get; set; }
    public virtual User User { get; set; }

    public Guid? RoleId { get; set; }
    public ProjectRole Role { get; set; }


    public ProjectMembership() { }

    public ProjectMembership(Guid projectId, Guid userId)
    {
        ProjectId = projectId;
        UserId = userId;
    }

    public class ProjectMembershipConfiguration : IEntityTypeConfiguration<ProjectMembership>
    {
        public void Configure(EntityTypeBuilder<ProjectMembership> builder)
        {
            builder.HasIndex(e => new { e.ProjectId, e.UserId }).IsUnique();

            builder
                .HasOne(tu => tu.Project)
                .WithMany(t => t.Memberships)
                .HasForeignKey(tu => tu.ProjectId);

            builder
                .HasOne(tu => tu.User)
                .WithMany(u => u.ProjectMemberships)
                .HasForeignKey(tu => tu.UserId)
                .HasPrincipalKey(u => u.Id);
        }
    }
}
