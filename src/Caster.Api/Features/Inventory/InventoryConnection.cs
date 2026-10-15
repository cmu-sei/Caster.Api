// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Runtime.Serialization;
using Caster.Api.Domain.Services.Inventory;
using Caster.Api.Infrastructure.Exceptions;

namespace Caster.Api.Features.Inventory
{
    /// <summary>
    /// A configured infrastructure connection. Its url and credentials are never
    /// returned.
    /// </summary>
    [DataContract(Name = "InventoryConnection")]
    public class InventoryConnection
    {
        /// <summary>
        /// The value to pass as the connection parameter of the other inventory
        /// endpoints. "default" for the top-level Infrastructure settings.
        /// </summary>
        public string Name { get; set; }

        public string Provider { get; set; }

        public bool Enabled { get; set; }

        /// <summary>
        /// True for the connection used when a request names none.
        /// </summary>
        public bool IsDefault { get; set; }

        /// <summary>
        /// When this connection's cache was last populated successfully, in UTC.
        /// Null if never.
        /// </summary>
        public DateTime? LastUpdated { get; set; }

        internal static InventoryConnection From(InventoryConnectionInfo info) => new()
        {
            Name = info.Name,
            Provider = info.Provider,
            Enabled = info.Enabled,
            IsDefault = info.IsDefault,
            LastUpdated = info.LastUpdated
        };

        /// <summary>
        /// The cached snapshot for <paramref name="connection"/>, the default when
        /// null or empty. A name that is not configured is a 404 rather than an
        /// unavailable result, so a mistyped name is not mistaken for a provider
        /// outage.
        /// </summary>
        internal static InventorySnapshot Snapshot(IInventoryService inventoryService, string connection) =>
            inventoryService.GetSnapshot(connection) ?? throw NotFound(connection);

        internal static EntityNotFoundException<InventoryConnection> NotFound(string connection) =>
            new($"Inventory connection '{connection}' is not configured.");
    }
}
