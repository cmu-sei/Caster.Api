// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Caster.Api.Domain.Models;

public class SystemRole
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public string Name { get; set; }

    public string Description { get; set; }

    public List<SystemPermissions> Permissions { get; set; }
}

public enum SystemPermissions
{
    All = 0,
    ReadOnly = 1,
    ManageSystem = 2,
    CreateProjects = 3,
    ManageProjects = 4,
    ImportProjects = 5,
    LockFiles = 6
}