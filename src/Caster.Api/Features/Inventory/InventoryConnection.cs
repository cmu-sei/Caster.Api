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
        /// endpoints: the connection's Name in Infrastructure:Connections.
        /// </summary>
        public string Name { get; set; }

        public string Provider { get; set; }

        /// <summary>
        /// Whether inventory is enabled for this connection.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// True for the connection used when a request names none: the first enabled
        /// one in Infrastructure:Connections.
        /// </summary>
        public bool IsDefault { get; set; }

        /// <summary>
        /// When this connection's cache was last populated successfully, in UTC.
        /// Null if never.
        /// </summary>
        public DateTime? LastUpdated { get; set; }

        /// <summary>
        /// The kinds of object this connection reads. The others are turned off in its
        /// settings, and their endpoints report them as not enabled.
        /// </summary>
        public InventoryCategory[] Resources { get; set; } = [];

        /// <summary>
        /// Why this connection's settings can't be used, in which case it isn't read.
        /// Null when they can.
        /// </summary>
        public string Error { get; set; }

        /// <summary>
        /// Missing when a project's assignment names a connection no longer in configuration;
        /// such a connection has no provider and is never read.
        /// </summary>
        public InventoryConnectionStatus Status { get; set; } = InventoryConnectionStatus.Available;

        /// <summary>
        /// The projects the connection is assigned to. Listed by GET api/inventory/connections
        /// only, which needs ViewInfrastructure.
        /// </summary>
        public InventoryConnectionProject[] Projects { get; set; } = [];

        /// <summary>From a project's routes: whether the connection is assigned to the project itself.</summary>
        public bool IsDirect { get; set; }

        /// <summary>From a project's routes: the project group the connection comes through, by name, if it does.</summary>
        public string[] ProjectGroups { get; set; } = [];

        internal static InventoryConnection From(InventoryConnectionInfo info) => new()
        {
            Name = info.Name,
            Provider = info.Provider,
            Enabled = info.Enabled,
            IsDefault = info.IsDefault,
            LastUpdated = info.LastUpdated,
            Resources = info.Resources,
            Error = info.Error
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

    public enum InventoryConnectionStatus
    {
        /// <summary>Configured. Its inventory may still be turned off: see Enabled.</summary>
        Available,

        /// <summary>Assigned to a project but no longer in configuration, e.g. renamed.</summary>
        Missing
    }

    public class InventoryConnectionProject
    {
        public Guid ProjectId { get; set; }
        public string Name { get; set; }
    }
}
