// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Caster.Api.Domain.Models;

public class ProjectRole : IEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public string Name { get; set; }

    public string Description { get; set; }

    public bool AllPermissions { get; set; }

    public List<ProjectPermission> Permissions { get; set; }
}

public enum ProjectPermission
{
    ViewProject,
    EditProject,
    ManageProject,
    ImportProject,
    LockFiles

}

public static class ProjectRoleDefaults
{
    public static Guid ProjectCreatorRoleId = new("1a3f26cd-9d99-4b98-b914-12931e786198");
    public static Guid ProjectReadOnlyRoleId = new("39aa296e-05ba-4fb0-8d74-c92cf3354c6f");
    public static Guid ProjectMemberRoleId = new("f870d8ee-7332-4f7f-8ee0-63bd07cfd7e4");
}

public class ProjectRoleConfiguration : IEntityTypeConfiguration<ProjectRole>
{
    public void Configure(EntityTypeBuilder<ProjectRole> builder)
    {
        builder.HasData(
            new ProjectRole
            {
                Id = ProjectRoleDefaults.ProjectCreatorRoleId,
                Name = "Manager",
                AllPermissions = true,
                Permissions = [],
                Description = "Can perform all actions on the Project"
            },
            new ProjectRole
            {
                Id = ProjectRoleDefaults.ProjectReadOnlyRoleId,
                Name = "Observer",
                AllPermissions = false,
                Permissions = [ProjectPermission.ViewProject],
                Description = "Has read only access to the Project"
            },
            new ProjectRole
            {
                Id = ProjectRoleDefaults.ProjectMemberRoleId,
                Name = "Member",
                AllPermissions = false,
                Permissions = [
                    ProjectPermission.ViewProject,
                    ProjectPermission.EditProject,
                    ProjectPermission.ImportProject
                ],
                Description = "Has read only access to the Project"
            }
        );
    }
}