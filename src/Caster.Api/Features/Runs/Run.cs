// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Features.Applies;
using Caster.Api.Features.Plans;

namespace Caster.Api.Features.Runs
{
    public class Run
    {
        /// <summary>
        /// A unique identifier
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// The unique identifier of the Workspace this Run was created for
        /// </summary>
        public Guid WorkspaceId { get; set; }

        /// <summary>
        /// Wether or not this Run was for a Destroy command
        /// </summary>
        public bool IsDestroy { get; set; }

        /// <summary>
        /// The current status of this Run
        /// </summary>
        public Domain.Models.RunStatus Status { get; set; }

        /// <summary>
        /// Optional list of resources to constrain the affects of this Run to
        /// </summary>
        public string[] Targets { get; set; }

        /// <summary>
        /// Optional list of resources to replace on this Run
        /// </summary>
        public string[] ReplaceAddresses { get; set; }

        /// <summary>
        /// The Plan for this Run, if one exists. Null if not requested
        /// </summary>
        public Plan Plan { get; set; }

        /// <summary>
        /// The Id of the Plan for this Run. Null if no Plan exists.
        /// </summary>
        public Guid? PlanId { get; set; }

        /// <summary>
        /// The Apply for this Run, if one exists. Null if not requested
        /// </summary>
        public Apply Apply { get; set; }

        /// <summary>
        /// The Id of the Apply for this Run. Null if no Apply exists.
        /// </summary>
        public Guid? ApplyId { get; set; }

        /// <summary>
        /// The time in UTC that this Run was initially created
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// The Id of the User who created this Run
        /// </summary>
        public Guid? CreatedById { get; set; }

        /// <summary>
        /// The Name of the User who created this Run
        /// </summary>
        public string CreatedBy { get; set; }

        /// <summary>
        /// The time in UTC that this Run was last modified (Applied, Rejected, etc)
        /// </summary>
        public DateTime? ModifiedAt { get; set; }

        /// <summary>
        /// The Id of the User who last modified this Run
        /// </summary>
        public Guid? ModifiedById { get; set; }

        /// <summary>
        /// The Name of the User who last modified this Run
        /// </summary>
        public string ModifiedBy { get; set; }
    }
}
