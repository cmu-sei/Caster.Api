// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Caster.Api.Domain.Models
{
    /// <summary>
    /// The VM templates and ISOs an exported archive's Terraform refers to. Caster only
    /// exports Terraform, so these must be copied to the target hypervisor by other means
    /// before the imported Workspaces can be applied.
    /// <para>
    /// Built by matching the string literals in the archive's files against the
    /// infrastructure inventory. A name only produced at plan time, for example by
    /// interpolation or a computed local, is not found.
    /// </para>
    /// </summary>
    public class DiskManifest
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        public List<DiskManifestEntry> Disks { get; set; } = [];

        /// <summary>
        /// Why the list may be incomplete, for example an inventory category that could
        /// not be read when the manifest was built.
        /// </summary>
        public List<string> Warnings { get; set; } = [];
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DiskKind
    {
        VmTemplate,
        Iso
    }

    public class DiskManifestEntry
    {
        public DiskKind Kind { get; set; }

        /// <summary>
        /// The value the Terraform refers to, e.g. a template name or "local:iso/file.iso".
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Inventory path, where the provider exposes one.
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// The infrastructure connection the disk was found on.
        /// </summary>
        public string Connection { get; set; }

        public string Provider { get; set; }

        /// <summary>
        /// Provider detail as discovered, e.g. storage, node, size.
        /// </summary>
        public Dictionary<string, string> Properties { get; set; } = [];

        /// <summary>
        /// Paths within the archive of the files that refer to the disk.
        /// </summary>
        public List<string> ReferencedBy { get; set; } = [];
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DiskStatus
    {
        /// <summary>Found in this system's infrastructure inventory.</summary>
        Present,

        /// <summary>Not found in this system's infrastructure inventory.</summary>
        Missing,

        /// <summary>The inventory could not be read, so it is not known.</summary>
        Unknown
    }

    /// <summary>
    /// A disk named by an imported archive's manifest, checked against this system.
    /// </summary>
    public class ImportedDisk
    {
        public DiskKind Kind { get; set; }
        public string Name { get; set; }
        public DiskStatus Status { get; set; }
        public List<string> ReferencedBy { get; set; } = [];
    }

    /// <summary>
    /// A file as it is placed in an archive, by its path within the archive.
    /// </summary>
    public record ArchiveFileEntry(string Path, string Content);
}
