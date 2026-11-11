// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;

namespace Caster.Api.Features.Workspaces
{
    public class Workspace
    {
        /// <summary>
        /// A unique identifier
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// The Name this Workspace will be referred to by
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The Id of the Directory that this Workspace was created in
        /// </summary>
        public Guid DirectoryId { get; set; }

        /// <summary>
        /// True if this Workspace will be dynamically assigned a Host on first Run
        /// </summary>
        public bool DynamicHost { get; set; }

        /// <summary>
        /// The version of Terraform that will be used for Runs in this Workspace.
        /// If null or empty, the default version will be used.
        /// </summary>
        public string TerraformVersion { get; set; }

        /// <summary>
        /// Limit the number of concurrent operations as Terraform walks the graph. 
        /// If null, the Terraform default will be used.
        /// </summary>
        public int? Parallelism { get; set; }

        /// <summary>
        /// If set, the number of consecutive failed destroys in an Azure Workspace before 
        /// Caster will attempt to mitigate by removing azurerm_resource_group children from the state.
        /// </summary>
        public int? AzureDestroyFailureThreshold { get; set; }

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
