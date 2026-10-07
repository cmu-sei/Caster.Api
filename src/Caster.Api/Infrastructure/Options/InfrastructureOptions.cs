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
    /// The infrastructure provider to query. Only "vsphere" is supported.
    /// </summary>
    public string Provider { get; set; } = InfrastructureProviders.Vsphere;

    /// <summary>
    /// Base url of the provider api, e.g. https://vcenter.example.com
    /// </summary>
    public string Url { get; set; }

    /// <summary>
    /// Read-only account used to query inventory.
    /// </summary>
    public string Username { get; set; }

    /// <summary>
    /// Password for <see cref="Username"/>. Write-only; never returned by the api.
    /// </summary>
    public string Password { get; set; }

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
}
