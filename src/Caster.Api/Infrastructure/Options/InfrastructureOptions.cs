// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;

namespace Caster.Api.Infrastructure.Options;

/// <summary>
/// Configuration for read-only discovery of the infrastructure Caster deploys to.
/// Empty by default; absent configuration must not affect startup.
/// </summary>
public class InfrastructureOptions
{
    /// <summary>
    /// The connections, each read and cached on its own, e.g.
    /// <c>Infrastructure:Connections:0:Name</c>. A connection inherits nothing from
    /// another, so one connection's credentials never reach another provider. The
    /// first enabled one is used when a request names none.
    /// </summary>
    public List<InfrastructureConnectionOptions> Connections { get; set; } = [];
}

/// <summary>
/// One infrastructure provider connection.
/// </summary>
public class InfrastructureConnectionOptions
{
    /// <summary>
    /// What clients call this connection, e.g. in <c>?connection=lab-proxmox</c>.
    /// Required, and unique across the connections, ignoring case.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The infrastructure provider to query. See <see cref="InfrastructureProviders"/>.
    /// </summary>
    public string Provider { get; set; } = InfrastructureProviders.Vsphere;

    /// <summary>
    /// Base url of the provider api, e.g. https://vcenter.example.com for vsphere
    /// or https://pve.example.com:8006 for proxmox. The proxmox reader appends
    /// /api2/json itself, and does not guess the port - include it when the api is
    /// not on 443.
    /// </summary>
    public string Url { get; set; }

    /// <summary>
    /// The account the inventory reads with. For proxmox this is the full
    /// user@realm, e.g. caster@pve.
    /// </summary>
    public string Username { get; set; }

    /// <summary>
    /// Password for <see cref="Username"/>. Write-only; never returned by the api.
    /// </summary>
    public string Password { get; set; }

    /// <summary>
    /// Proxmox api token, as a single opaque string in exactly the form proxmox
    /// prints when the token is created: <c>user@realm!tokenid=secret</c>. It is
    /// sent verbatim as <c>Authorization: PVEAPIToken={ApiToken}</c>.
    /// <para>
    /// Kept as one value rather than id + secret so the whole credential can be
    /// injected as a single secret, and so it can be pasted straight out of the
    /// proxmox ui without being split up by hand. Preferred over
    /// <see cref="Username"/> / <see cref="Password"/> when both are set, because
    /// a token can be scoped to just the privileges it needs and never expires mid-poll.
    /// </para>
    /// Ignored by the vsphere provider. Write-only; never returned by the api.
    /// </summary>
    public string ApiToken { get; set; }

    /// <summary>
    /// Skip TLS certificate validation when connecting to the provider.
    /// </summary>
    public bool InsecureSkipVerify { get; set; }

    /// <summary>
    /// Where this connection's ISOs live. The inventory lists only these, so it offers
    /// the stock ISOs rather than everything uploaded alongside them.
    /// <para>
    /// For proxmox each entry is a storage name with ISO content, e.g.
    /// <c>iso-stock</c>, or the start of a volid, e.g.
    /// <c>local:iso/00000000-0000-0000-0000-000000000000__</c> for the ISOs
    /// TopoMojo shares with every workspace. Proxmox ISO names have no folders.
    /// Empty reads every ISO storage.
    /// </para>
    /// <para>
    /// For vsphere each entry is a datastore name, e.g. <c>iso-datastore</c>, or a
    /// folder on one in datastore path form, e.g. <c>[iso-datastore] stock</c>.
    /// Each entry is searched with its subfolders, and each search is a vCenter
    /// task. Empty lists no ISOs.
    /// </para>
    /// </summary>
    public string[] IsoLocations { get; set; } = [];

    /// <summary>
    /// vsphere only. The datacenters this connection reads, by name, e.g. <c>DC1</c>.
    /// Empty reads all of them. Content libraries belong to the whole vCenter, so this
    /// doesn't filter them; use their Include and Exclude instead.
    /// </summary>
    public string[] Datacenters { get; set; } = [];

    /// <summary>
    /// What the inventory reads from this connection, and how often.
    /// </summary>
    public InventoryOptions Inventory { get; set; } = new();
}

/// <summary>
/// One connection's inventory settings. Everything here only affects what the inventory
/// lists; where things live (<see cref="InfrastructureConnectionOptions.IsoLocations"/>,
/// <see cref="InfrastructureConnectionOptions.Datacenters"/>) is set on the connection.
/// </summary>
public class InventoryOptions
{
    /// <summary>
    /// When false, the provider is never contacted and the inventory endpoints report
    /// this connection as not enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// How often the background cache is refreshed, in minutes.
    /// </summary>
    public int RefreshMinutes { get; set; } = 30;

    /// <summary>
    /// Which kinds of object are read, and filters on each.
    /// </summary>
    public InventoryResourcesOptions Resources { get; set; } = new();
}

/// <summary>
/// The kinds of object the inventory lists. Each is on by default.
/// </summary>
public class InventoryResourcesOptions
{
    public InventoryResourceOptions VmTemplates { get; set; } = new();

    /// <summary>
    /// <see cref="InventoryResourceOptions.Locations"/> doesn't apply: ISOs are found in
    /// <see cref="InfrastructureConnectionOptions.IsoLocations"/>.
    /// </summary>
    public InventoryResourceOptions Isos { get; set; } = new();

    public InventoryResourceOptions Networks { get; set; } = new();

    public InventoryResourceOptions Datastores { get; set; } = new();

    /// <summary>
    /// vsphere only. <see cref="InventoryResourceOptions.Locations"/> doesn't apply.
    /// </summary>
    public InventoryResourceOptions ContentLibraries { get; set; } = new();

    public InventoryResourceOptions Hosts { get; set; } = new();
}

/// <summary>
/// Whether one kind of object is read, and which of them are listed. An object is
/// listed when it is in one of the <see cref="Locations"/> (or there are none), its
/// name matches <see cref="Include"/> (or there is none), and its name doesn't match
/// <see cref="Exclude"/>.
/// </summary>
public class InventoryResourceOptions
{
    /// <summary>
    /// When false this kind of object isn't read, and its endpoint reports it as not
    /// enabled. Data another kind needs is still read.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Where to list objects from, in the provider's own terms.
    /// <para>
    /// vsphere: a full inventory path, e.g. <c>/DC1/vm/Templates</c>, matching that
    /// folder and everything below it, ignoring case. Networks also take a
    /// distributed switch's name, matching its port groups.
    /// </para>
    /// <para>
    /// proxmox: a name, ignoring case. VM templates take a node or resource pool,
    /// networks a node (bridges) or SDN zone (vnets), datastores a storage, and hosts
    /// a node.
    /// </para>
    /// </summary>
    public string[] Locations { get; set; } = [];

    /// <summary>
    /// A regular expression the name must match, ignoring case.
    /// </summary>
    public string Include { get; set; }

    /// <summary>
    /// A regular expression that leaves out every name it matches, ignoring case.
    /// Wins over <see cref="Include"/>.
    /// </summary>
    public string Exclude { get; set; }
}

public static class InfrastructureProviders
{
    public const string Vsphere = "vsphere";

    public const string Proxmox = "proxmox";

    /// <summary>
    /// Every provider Caster can read. Keep in step with the
    /// <c>IInventoryProvider</c> implementations registered in
    /// <c>AddInventoryServices</c>; it is only used to build the
    /// "provider not supported" message.
    /// </summary>
    public static readonly string[] All = [Vsphere, Proxmox];
}
