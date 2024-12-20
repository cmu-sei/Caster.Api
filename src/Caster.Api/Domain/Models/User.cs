// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Caster.Api.Domain.Models
{
    public class User : IEntity
    {
        [Key]
        public Guid Id { get; set; }
        public string Name { get; set; }

        public Guid? RoleId { get; set; }
        public virtual SystemRole Role { get; set; }

        public ICollection<ProjectMembership> ProjectMemberships { get; set; } = new List<ProjectMembership>();
        public ICollection<GroupMembership> GroupMemberships { get; set; } = new List<GroupMembership>();
    }
}

