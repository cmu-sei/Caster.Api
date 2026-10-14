// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;

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

        /// <summary>Why the last sync failed, or null if it succeeded.</summary>
        public string Error { get; set; }
    }
}
