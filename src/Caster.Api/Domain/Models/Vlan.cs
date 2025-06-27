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

        private bool _reserved;
        public bool Reserved
        {
            get
            {
                return _reserved;
            }

            set
            {
                if (ReservedEditable)
                {
                    _reserved = value;
                }
                else if (value != _reserved)
                {
                    throw new ArgumentException("Cannot edit Reserved when ReservedEditable is false. This VLAN was likely set to Reserved in SeedData and must be removed there to edit it's Reserved status");
                }
            }
        }
        public bool ReservedEditable { get; set; } = true;
    }

    public class VlanConfiguration : IEntityTypeConfiguration<Vlan>
    {
        public void Configure(EntityTypeBuilder<Vlan> builder)
        {
            builder.HasIndex(x => x.VlanId);
            builder.Property(x => x.ReservedEditable).HasDefaultValue(true);
        }
    }
}

