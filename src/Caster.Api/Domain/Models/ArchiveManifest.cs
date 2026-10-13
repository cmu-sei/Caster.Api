// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;

namespace Caster.Api.Domain.Models
{
    /// <summary>
    /// Settings that travel with an exported archive so that they can be restored on Import.
    /// Written to a single reserved entry at the root of the archive. Archives created before
    /// this existed do not contain one, so every consumer must treat it as optional.
    /// </summary>
    public class ArchiveManifest
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        public ArchiveManifestSource Source { get; set; }

        /// <summary>
        /// Keyed by the Directory's path within the archive, without a trailing separator.
        /// The exported root of a Directory export is keyed by the empty string.
        /// </summary>
        public Dictionary<string, ArchiveManifestDirectory> Directories { get; set; } =
            new(StringComparer.Ordinal);

        /// <summary>
        /// Keyed by the Workspace's path within the archive, including the
        /// __Workspaces__ segment, without a trailing separator.
        /// </summary>
        public Dictionary<string, ArchiveManifestWorkspace> Workspaces { get; set; } =
            new(StringComparer.Ordinal);
    }

    public class ArchiveManifestSource
    {
        /// <summary>
        /// "project" or "directory"
        /// </summary>
        public string Type { get; set; }

        public string Name { get; set; }
    }

    public class ArchiveManifestDirectory
    {
        public string TerraformVersion { get; set; }
        public int? Parallelism { get; set; }
        public bool? AzureDestroyFailureThresholdEnabled { get; set; }
        public int? AzureDestroyFailureThreshold { get; set; }
    }

    public class ArchiveManifestWorkspace
    {
        public string TerraformVersion { get; set; }
        public int? Parallelism { get; set; }
        public int? AzureDestroyFailureThreshold { get; set; }
    }
}
