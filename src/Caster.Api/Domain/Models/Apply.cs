// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.Serialization;

namespace Caster.Api.Domain.Models
{
    public class Apply : IEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        public Guid RunId { get; set; }
        public virtual Run Run { get; set; }

        public ApplyStatus Status { get; set; }
        public string Output { get; set; }
    }

    public enum ApplyStatus
    {
        Queued = 0,
        Failed = 1,
        Applying = 2,
        Applied = 3,

        [EnumMember(Value = "Applied - State Error")]
        Applied_StateError = 4,

        [EnumMember(Value = "Failed - State Error")]
        Failed_StateError = 5
    }
}
