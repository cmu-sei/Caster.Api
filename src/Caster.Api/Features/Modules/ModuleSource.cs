// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Domain.Services.Modules;

namespace Caster.Api.Features.Modules
{
    /// <summary>
    /// A configured module source and how its last sync went.
    /// </summary>
    public class ModuleSource
    {
        /// <summary>The source's name, which prefixes the Path of every Module it provides.</summary>
        public string Name { get; set; }

        public string Description { get; set; }

        /// <summary>The kind of repository: git or gitlab.</summary>
        public string Provider { get; set; }

        /// <summary>When this API instance last tried to sync the source, or null if it has not yet.</summary>
        public DateTime? LastAttempt { get; set; }

        /// <summary>When this API instance last synced the source successfully, or null if it has not yet.</summary>
        public DateTime? LastSuccess { get; set; }

        /// <summary>
        /// What the last sync found: Ok, Empty (read fine, no Modules yet), Warning (read fine, but its
        /// Modules cannot be used) or Error. Null if this API instance has not synced the source yet.
        /// </summary>
        public ModuleSourceState? State { get; set; }

        /// <summary>Human-readable detail for Empty, Warning and Error (equal to Error then); null for Ok.</summary>
        public string Message { get; set; }

        /// <summary>
        /// Why the last sync failed: Message when State is Error, otherwise null, so a UI that reads only this
        /// does not show an Empty or Warning source as a failure. Deprecated; use State and Message.
        /// </summary>
        [Obsolete("Use State and Message.")]
        public string Error { get; set; }
    }
}
