// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Caster.Api.Domain.Models
{
    public class Project : IEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        public string Name { get; set; }

        public virtual ICollection<Directory> Directories { get; set; } = new List<Directory>();

        public Guid? PartitionId { get; set; }
        public virtual Partition Partition { get; set; }

        public virtual ICollection<ProjectMembership> Memberships { get; set; } = new List<ProjectMembership>();

        public Project() { }

        public Project(string name)
        {
            this.Name = name;
        }
    }
}
