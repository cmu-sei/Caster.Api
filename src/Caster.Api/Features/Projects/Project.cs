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
        /// The vlan partition this project is a part of.
        /// </summary>
        public Guid? PartitionId { get; set; }
    }
}
