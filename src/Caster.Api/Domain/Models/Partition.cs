// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Caster.Api.Domain.Models
{
    public class Partition : IEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        public Guid PoolId { get; set; }
        public virtual Pool Pool { get; set; }

        public string Name { get; set; }
        public bool IsDefault { get; set; }
        public virtual ICollection<Vlan> Vlans { get; set; } = new List<Vlan>();
    }
}

