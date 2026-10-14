// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;

namespace Caster.Api.Domain.Models
{
    public abstract class ArchiveExtractResult
    {
        /// <summary>
        /// The manifest found in the archive, or null if it did not contain one.
        /// </summary>
        public ArchiveManifest Manifest { get; set; }

        /// <summary>
        /// Settings that were present in the manifest but could not be applied,
        /// for example a Terraform version that is not available on this system.
        /// </summary>
        public List<string> SkippedSettings { get; } = new();

        /// <summary>
        /// Non-fatal problems with the archive itself.
        /// </summary>
        public List<string> Warnings { get; } = new();
    }

    public class ArchiveExtractResult<T> : ArchiveExtractResult
    {
        public T Entity { get; set; }
    }
}
