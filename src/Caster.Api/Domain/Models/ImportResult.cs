// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;

namespace Caster.Api.Domain.Models
{
    public class ImportResult
    {
        public IEnumerable<File> LockedFiles { get; set; }

        /// <summary>
        /// Settings carried by the archive that could not be applied, for example a
        /// Terraform version that is not available on this system.
        /// </summary>
        public IEnumerable<string> SkippedSettings { get; set; } = [];

        /// <summary>
        /// Non-fatal problems with the archive itself.
        /// </summary>
        public IEnumerable<string> Warnings { get; set; } = [];
    }
}
