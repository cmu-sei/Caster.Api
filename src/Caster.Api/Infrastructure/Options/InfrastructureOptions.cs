// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Caster.Api.Infrastructure.Options;

/// <summary>
/// Configuration for read-only discovery of the infrastructure Caster deploys to.
/// Disabled by default; absent configuration must not affect startup.
/// </summary>
public class InfrastructureOptions
{
    /// <summary>
    /// Master switch. When false, no connection is attempted and the inventory
    /// endpoints report themselves as unavailable.
    /// </summary>
    public bool Enabled { get; set; } = false;

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
    /// Read-only account used to query inventory. For proxmox this is the full
    /// user@realm, e.g. caster-ro@pve.
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
    /// a token can be scoped to read-only privileges and never expires mid-poll.
    /// </para>
    /// Ignored by the vsphere provider. Write-only; never returned by the api.
    /// </summary>
    public string ApiToken { get; set; }

    /// <summary>
    /// Skip TLS certificate validation when connecting to the provider.
    /// </summary>
    public bool InsecureSkipVerify { get; set; }

    /// <summary>
    /// How often the background cache is refreshed, in minutes.
    /// </summary>
    public int RefreshMinutes { get; set; } = 30;
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
