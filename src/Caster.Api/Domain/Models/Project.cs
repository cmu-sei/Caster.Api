// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Caster.Api.Domain.Models
{
    public class Project : IEntity, Services.Capacity.IDeploymentLimits, Services.Capacity.IDeploymentLimitsTarget
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public DateTime DateCreated { get; set; }

        /// <summary>The most machines each workspace in this project may have. Null for no limit at this level. See <see cref="Services.Capacity.DeploymentLimitResolver"/>.</summary>
        public int? MaxMachines { get; set; }

        /// <summary>The most vCPUs its machines may have between them. Null for no limit at this level.</summary>
        public int? MaxVcpu { get; set; }

        /// <summary>The most memory, in GiB, its machines may have between them. Null for no limit at this level.</summary>
        [Column("max_memory_gib")]
        public int? MaxMemoryGiB { get; set; }

        /// <summary>The most disk, in GiB, its machines may have between them. Null for no limit at this level.</summary>
        [Column("max_disk_gib")]
        public int? MaxDiskGiB { get; set; }

        public virtual ICollection<Directory> Directories { get; set; } = new List<Directory>();

        public Guid? PartitionId { get; set; }
        public virtual Partition Partition { get; set; }

        public virtual ICollection<ProjectMembership> Memberships { get; set; } = new List<ProjectMembership>();

        public Project() { }

        public Project(string name)
        {
            this.Name = name;
            this.DateCreated = DateTime.UtcNow;
        }
    }
}
