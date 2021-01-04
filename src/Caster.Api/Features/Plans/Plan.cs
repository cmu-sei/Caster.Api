// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Plans
{
    public class Plan
    {
        /// <summary>
        /// A unique identifier
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// The unique identifier of the Run this Plan is associated with
        /// </summary>
        public Guid RunId { get; set; }

        /// <summary>
        /// The current status of this Plan
        /// </summary>
        public PlanStatus Status { get; set; }

        /// <summary>
        /// The raw Terraform output of the Plan command
        /// </summary>
        public string Output { get; set; }
    }
}

