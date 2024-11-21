// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.OpenApi.Extensions;

namespace Caster.Api.Domain.Models;

public class SystemRole : IEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public bool AllPermissions { get; set; }
    public bool Immutable { get; set; }

    public List<SystemPermission> Permissions { get; set; }
}

public enum SystemPermission
{
    CreateProjects,
    ViewProjects,
    EditProjects,
    ManageProjects,
    ImportProjects,
    LockFiles,
    ViewUsers,
    ManageUsers,
    ViewWorkspaces,
    ManageWorkspaces,
    ViewVLANs,
    ManageVLANs,
    ViewRoles,
    ManageRoles,
    ViewGroups,
    ManageGroups,
    ViewHosts,
    ManageHosts,
    ViewModules,
    ManageModules
}

public static class SystemRoleDefaults
{
    public static Guid AdministratorRoleId = new("f35e8fff-f996-4cba-b303-3ba515ad8d2f");
}

public class SystemRoleConfiguration : IEntityTypeConfiguration<SystemRole>
{
    public void Configure(EntityTypeBuilder<SystemRole> builder)
    {
        builder.HasIndex(x => x.Name).IsUnique();

        builder.HasData(
            new SystemRole
            {
                Id = SystemRoleDefaults.AdministratorRoleId,
                Name = "Administrator",
                AllPermissions = true,
                Immutable = true,
                Permissions = [],
                Description = "Can perform all actions"
            }
        );
    }
}