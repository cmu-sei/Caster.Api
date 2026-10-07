// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;

namespace Caster.Api.Domain.Services.Inventory;

/// <summary>
/// The kinds of infrastructure objects Caster can discover.
/// </summary>
public enum InventoryCategory
{
    VmTemplate,
    Iso,
    Network,
    Datastore
}

/// <summary>
/// A single discovered infrastructure object. Provider agnostic.
/// </summary>
public class InventoryEntry
{
    /// <summary>
    /// Provider native identifier, e.g. a vSphere managed object id like "vm-1024".
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// Display name, which is also the value a terraform variable normally expects.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Inventory path, where the provider exposes one. Falls back to Name.
    /// </summary>
    public string Path { get; set; }

    /// <summary>
    /// Extra provider detail, flattened to strings so the shape stays stable.
    /// </summary>
    public Dictionary<string, string> Properties { get; set; } = [];
}

/// <summary>
/// The result of reading one <see cref="InventoryCategory"/>. Categories fail
/// independently, so a datastore permission problem does not hide networks.
/// </summary>
public class InventoryCategoryResult
{
    public InventoryEntry[] Entries { get; set; } = [];

    /// <summary>
    /// False when the category could not be read, or is not supported.
    /// </summary>
    public bool Available { get; set; }

    /// <summary>
    /// Human readable reason, set when <see cref="Available"/> is false.
    /// Must never contain credentials.
    /// </summary>
    public string Error { get; set; }

    public static InventoryCategoryResult Ok(InventoryEntry[] entries) =>
        new() { Entries = entries ?? [], Available = true };

    public static InventoryCategoryResult Unavailable(string error) =>
        new() { Entries = [], Available = false, Error = error };
}

/// <summary>
/// One complete read of the provider inventory.
/// </summary>
public class InventorySnapshot
{
    /// <summary>
    /// When the data was read, in UTC. Null when the inventory has never been
    /// read successfully.
    /// </summary>
    public DateTime? LastUpdated { get; set; }

    public InventoryCategoryResult VmTemplates { get; set; } = InventoryCategoryResult.Unavailable(InventoryMessages.NotLoaded);
    public InventoryCategoryResult Isos { get; set; } = InventoryCategoryResult.Unavailable(InventoryMessages.NotLoaded);
    public InventoryCategoryResult Networks { get; set; } = InventoryCategoryResult.Unavailable(InventoryMessages.NotLoaded);
    public InventoryCategoryResult Datastores { get; set; } = InventoryCategoryResult.Unavailable(InventoryMessages.NotLoaded);

    public InventoryCategoryResult Get(InventoryCategory category) => category switch
    {
        InventoryCategory.VmTemplate => VmTemplates,
        InventoryCategory.Iso => Isos,
        InventoryCategory.Network => Networks,
        InventoryCategory.Datastore => Datastores,
        _ => InventoryCategoryResult.Unavailable($"Unknown inventory category '{category}'.")
    };

    /// <summary>
    /// Builds a snapshot where every category carries the same failure reason,
    /// used when the provider connection itself could not be established.
    /// </summary>
    public static InventorySnapshot AllUnavailable(string error, DateTime? lastUpdated = null) => new()
    {
        LastUpdated = lastUpdated,
        VmTemplates = InventoryCategoryResult.Unavailable(error),
        Isos = InventoryCategoryResult.Unavailable(error),
        Networks = InventoryCategoryResult.Unavailable(error),
        Datastores = InventoryCategoryResult.Unavailable(error)
    };
}

public static class InventoryDefaults
{
    /// <summary>
    /// Upper bound on how long a forced refresh waits for the background read to
    /// finish before it gives up waiting. The read itself keeps going, so a slow
    /// or unreachable provider can never hang the http request. Deliberately a
    /// constant rather than a config key.
    /// </summary>
    public static readonly TimeSpan ForceRefreshTimeout = TimeSpan.FromSeconds(15);
}

public static class InventoryMessages
{
    public const string NotLoaded = "Infrastructure inventory has not been loaded yet.";

    public const string NotEnabled = "Infrastructure inventory is not enabled. Set Infrastructure:Enabled to true and supply a provider connection.";

    public const string NotConfigured = "Infrastructure inventory is enabled but the provider connection is incomplete. Infrastructure:Url, Infrastructure:Username and Infrastructure:Password are all required.";

    /// <summary>
    /// ISO discovery requires datastore file browsing. The vSphere Automation
    /// REST api does not expose it, so the category is reported as unsupported
    /// rather than silently empty. Tracked as CRU-2828.
    /// </summary>
    public const string IsoNotSupported = "ISO discovery is not supported by the vSphere Automation REST API. Listing ISO files requires datastore file browsing, which is only available through the vim25 (SOAP) API. Enter ISO paths manually for now.";

    public static string UnsupportedProvider(string provider) =>
        $"Infrastructure provider '{provider}' is not supported. Only 'vsphere' is implemented.";
}
