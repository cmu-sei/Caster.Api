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
    public class Host : IEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        public string Name { get; set; }
        public string Datastore { get; set; }
        public int MaximumMachines { get; set; }
        public bool Enabled { get; set; }
        public bool Development { get; set; }

        public Guid? ProjectId { get; set; }
        public virtual Project Project { get; set; }

        /// <summary>
        /// The project group the host serves, if any. A host has at most one owner, a project
        /// or a group; unowned hosts serve no one.
        /// </summary>
        public Guid? ProjectGroupId { get; set; }
        public virtual ProjectGroup ProjectGroup { get; set; }

        public virtual ICollection<HostMachine> Machines { get; set; } = new HashSet<HostMachine>();

        public class HostConfiguration : IEntityTypeConfiguration<Host>
        {
            public void Configure(EntityTypeBuilder<Host> builder)
            {
                builder.ToTable(x => x.HasCheckConstraint(
                    "CK_hosts_one_owner",
                    "project_id IS NULL OR project_group_id IS NULL"));
            }
        }

        public File GetHostFile()
        {
            return new File
            {
                Name = "generated_host_values.auto.tfvars",
                Content = $"vsphere_host_name = \"{Name}\"\nvsphere_datastore = \"{Datastore}\""
            };
        }
    }

    public class HostMachine
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        public string Name { get; set; }

        public Guid WorkspaceId { get; set; }
        public virtual Workspace Workspace { get; set; }

        public Guid HostId { get; set; }
        public virtual Host Host { get; set; }
    }
}
