// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Runtime.Serialization;

namespace Caster.Api.Features.Inventory
{
    /// <summary>
    /// The outcome of a forced inventory refresh.
    /// </summary>
    [DataContract(Name = "InventoryRefreshResult")]
    public class InventoryRefreshResult
    {
        /// <summary>
        /// True when the refresh finished before the wait bound expired. When true,
        /// the inventory get endpoints are guaranteed to return the new data.
        /// When false the refresh is still running in the background, and the get
        /// endpoints will keep returning the previous snapshot until it lands.
        /// </summary>
        public bool Completed { get; set; }

        /// <summary>
        /// The snapshot timestamp after the wait, in UTC. Null if the inventory has
        /// never been read successfully. Unchanged from the previous value when
        /// <see cref="Completed"/> is false, or when the refresh itself failed.
        /// </summary>
        public DateTime? LastUpdated { get; set; }

        /// <summary>
        /// How long the server waited for the refresh before returning, in seconds.
        /// A client that gets Completed = false can use this to size its polling.
        /// </summary>
        public int WaitSeconds { get; set; }
    }
}
