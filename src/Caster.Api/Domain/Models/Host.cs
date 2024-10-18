// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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

        public virtual ICollection<HostMachine> Machines { get; set; } = new HashSet<HostMachine>();

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
