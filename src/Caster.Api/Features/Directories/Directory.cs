// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using Caster.Api.Features.Designs;
using Caster.Api.Features.Files;
using Caster.Api.Features.Workspaces;

namespace Caster.Api.Features.Directories
{
    public class Directory
    {
        /// <summary>
        /// Id of the directory.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Name of the directory.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Id of the project this directory is under.
        /// </summary>

        public Guid ProjectId { get; set; }
        /// <summary>
        /// Optional Id of the directory this directory is under
        /// </summary>
        public Guid? ParentId { get; set; }

        /// <summary>
        /// List of files in the directory. Null if not requested
        /// </summary>
        public List<File> Files { get; set; }

        /// <summary>
        /// List of workspaces in the directory. Null if not requested
        /// </summary>
        public List<Workspace> Workspaces { get; set; }

        /// <summary>
        /// List of designs in the directory. Null if not requested
        /// </summary>
        public List<Design> Designs { get; set; }

        /// <summary>
        /// The version of Terraform that will be set Workspaces created in this Directory.
        /// If not set, will traverse parents until a version is found.
        /// If still not set, the default version will be used.
        /// </summary>
        public string TerraformVersion { get; set; }
    }
}
