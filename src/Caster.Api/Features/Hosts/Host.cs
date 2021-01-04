// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;

namespace Caster.Api.Features.Hosts
{
    public class Host
    {
        /// <summary>
        /// ID of the Host.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Name of the Host.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The name of the datastore to use for this Host
        /// </summary>
        public string Datastore { get; set; }

        /// <summary>
        /// The maximum number of machines to deploy to this Host for DynamicHost Workspaces
        /// </summary>
        public int MaximumMachines { get; set; }

        /// <summary>
        /// If true, use this Host when deploying to a DynamicHost Workspace
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// If true, use this Host only for manual deployments
        /// </summary>
        public bool Development { get; set; }

        /// <summary>
        /// The Project this Host is assigned to, if any
        /// </summary>
        public Guid? ProjectId { get; set; }
    }
}
