// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;

namespace Caster.Api.Features.Projects
{
    public class Project
    {
        /// <summary>
        /// ID of the project.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Name of the project.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Description of the project.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Date the project was created.
        /// </summary>
        public DateTime DateCreated { get; set; }

        /// <summary>
        /// The vlan partition this project is a part of.
        /// </summary>
        public Guid? PartitionId { get; set; }

        /// <summary>
        /// The most machines this sets, or null for no limit here. The limit in
        /// force is the lowest of the workspace's, its project's and the global one.
        /// </summary>
        public int? MaxMachines { get; set; }

        /// <summary>The most vCPUs this sets, or null for no limit here.</summary>
        public int? MaxVcpu { get; set; }

        /// <summary>The most memory, in GiB, this sets, or null for no limit here.</summary>
        public int? MaxMemoryGiB { get; set; }

        /// <summary>The most disk, in GiB, this sets, or null for no limit here.</summary>
        public int? MaxDiskGiB { get; set; }
    }
}
