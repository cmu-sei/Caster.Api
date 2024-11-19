// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Caster.Api.Domain.Models;

public class SystemRole : IEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public string Name { get; set; }

    public string Description { get; set; }

    public bool AllPermissions { get; set; }

    public List<SystemPermissions> Permissions { get; set; }
}

public enum SystemPermissions
{
    CreateProjects,
    ViewProjects,
    EditProjects,
    ManageProjects,
    ImportProjects,
    LockFiles,
    ViewUsers,
    EditUsers,
    ViewWorkspaces,
    EditWorkspaces,
    ViewVLANs,
    EditVLANs,
    ViewRoles,
    EditRoles,
    ViewGroups,
    EditGroups,
    ViewHosts,
    EditHosts,
    ViewModules,
    EditModules
}