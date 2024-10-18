// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Caster.Api.Domain.Models
{
    public class Vlan : IEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }
        public Guid PoolId { get; set; }
        public Guid? PartitionId { get; set; }
        public int VlanId { get; set; }
        public bool InUse { get; set; }
        public string Tag { get; set; }
        public bool Reserved { get; set; }
    }

    public class VlanConfiguration : IEntityTypeConfiguration<Vlan>
    {
        public void Configure(EntityTypeBuilder<Vlan> builder)
        {
            builder.HasIndex(x => x.VlanId);
        }
    }
}

