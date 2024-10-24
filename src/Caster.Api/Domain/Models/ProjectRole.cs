// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Caster.Api.Domain.Models;

public class ProjectRole
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public string Name { get; set; }

    public string Description { get; set; }

    public List<ProjectPermissions> Permissions { get; set; }
}

public enum ProjectPermissions
{
    All = 0,
    ReadOnly = 1,
}

public static class ProjectRoleDefaults
{
    public static Guid ProjectCreatorRoleId = new("1a3f26cd-9d99-4b98-b914-12931e786198");
    public static Guid ProjectReadOnlyRoleId = new("39aa296e-05ba-4fb0-8d74-c92cf3354c6f");
}

public class ProjectRoleConfiguration : IEntityTypeConfiguration<ProjectRole>
{
    public void Configure(EntityTypeBuilder<ProjectRole> builder)
    {
        builder.HasData(
            new ProjectRole
            {
                Id = ProjectRoleDefaults.ProjectCreatorRoleId,
                Name = "Administrator",
                Permissions = [ProjectPermissions.All],
                Description = "Can perform all actions on the Project"
            },
            new ProjectRole
            {
                Id = ProjectRoleDefaults.ProjectReadOnlyRoleId,
                Name = "Observer",
                Permissions = [ProjectPermissions.ReadOnly],
                Description = "Has read only access to the Project"
            }
        );
    }
}