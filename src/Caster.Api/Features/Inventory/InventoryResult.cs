// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Caster.Api.Domain.Services.Inventory;

namespace Caster.Api.Features.Inventory
{
    /// <summary>
    /// A single discovered infrastructure object.
    /// </summary>
    [DataContract(Name = "InventoryItem")]
    public class InventoryItem
    {
        /// <summary>
        /// Provider native identifier, e.g. a vSphere managed object id like "vm-1024".
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Display name. This is normally the value a terraform variable expects.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Inventory path, where the provider exposes one. Falls back to the name.
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// Extra provider detail, flattened to strings.
        /// </summary>
        public Dictionary<string, string> Properties { get; set; } = [];

        /// <summary>
        /// Terraform that references this object, ready to paste, keyed by the
        /// provider source it is written for, e.g. "bpg/proxmox". Empty for objects
        /// that are not referenced this way.
        /// </summary>
        public Dictionary<string, string> Terraform { get; set; } = [];
    }

    /// <summary>
    /// The cached result of an infrastructure inventory read.
    /// </summary>
    [DataContract(Name = "InventoryResult")]
    public class InventoryResult
    {
        public InventoryItem[] Items { get; set; } = [];

        /// <summary>
        /// When the items were read, in UTC. Null if never.
        /// </summary>
        public DateTime? LastUpdated { get; set; }

        /// <summary>
        /// False when the inventory is not configured, not supported, or could not
        /// be read and no earlier read is kept.
        /// </summary>
        public bool Available { get; set; }

        /// <summary>
        /// False when the provider has no such kind of object, e.g. content
        /// libraries on Proxmox, so there is nothing to show rather than nothing
        /// that could be read.
        /// </summary>
        public bool Applies { get; set; } = true;

        /// <summary>
        /// True when the latest read failed and the items are from the last read
        /// that succeeded, at <see cref="LastUpdated"/>. They may be out of date.
        /// </summary>
        public bool Stale { get; set; }

        /// <summary>
        /// Human readable reason, set when <see cref="Available"/> is false or
        /// <see cref="Stale"/> is true.
        /// </summary>
        public string Error { get; set; }

        internal static InventoryResult From(InventorySnapshot snapshot, InventoryCategory category)
        {
            var result = snapshot.Get(category);

            return new InventoryResult
            {
                Items = result.Entries
                    .Select(x => new InventoryItem
                    {
                        Id = x.Id,
                        Name = x.Name,
                        Path = x.Path,
                        Properties = x.Properties ?? [],
                        Terraform = x.Terraform ?? []
                    })
                    .ToArray(),
                LastUpdated = result.LastUpdated ?? snapshot.LastUpdated,
                Available = result.Available,
                Applies = result.Applies,
                Stale = result.Stale,
                Error = result.Error
            };
        }
    }
}
