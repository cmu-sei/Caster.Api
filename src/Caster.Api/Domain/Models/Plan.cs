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
    public class Plan : IEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        public Guid RunId { get; set; }
        public virtual Run Run { get; set; }

        public PlanStatus Status { get; set; }
        public string Output { get; set; }

        /// <summary>
        /// JSON of the <see cref="Topology"/> read from this plan, or null when
        /// the plan failed or topology is turned off. Kept out of the Plan
        /// payload; served by its own endpoint.
        /// </summary>
        public string Topology { get; set; }
    }

    public enum PlanStatus
    {
        Queued = 0,
        Failed = 1,
        Rejected = 2,
        PrePlanning = 6,
        Planning = 3,
        PostPlanning = 7,
        Planned = 4,
        Initializing = 5,
    }
}

