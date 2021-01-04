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
    }
}
