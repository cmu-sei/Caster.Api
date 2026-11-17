// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Options;

namespace Caster.Api.Domain.Services.EnvironmentSets;

/// <summary>One entry a preset makes: an environment variable taking a connection field.</summary>
public record PresetEntry(string TargetName, ConnectionField Field);

/// <summary>
/// The entries a Terraform provider needs to reach a connection, through the provider's own
/// environment variables. The only provider-specific part of connection entries.
/// </summary>
public record ConnectionPreset(string Id, string Name, string Provider, Func<InfrastructureConnectionOptions, PresetEntry[]> Entries);

public static class ConnectionPresets
{
    public static readonly ConnectionPreset[] All =
    [
        new("bpg/proxmox", "bpg/proxmox", InfrastructureProviders.Proxmox, connection =>
            HasToken(connection)
                ?
                [
                    new("PROXMOX_VE_ENDPOINT", ConnectionField.Url),
                    new("PROXMOX_VE_INSECURE", ConnectionField.Insecure),
                    new("PROXMOX_VE_API_TOKEN", ConnectionField.ApiToken)
                ]
                :
                [
                    new("PROXMOX_VE_ENDPOINT", ConnectionField.Url),
                    new("PROXMOX_VE_INSECURE", ConnectionField.Insecure),
                    new("PROXMOX_VE_USERNAME", ConnectionField.Username),
                    new("PROXMOX_VE_PASSWORD", ConnectionField.Password)
                ]),

        new("telmate/proxmox", "Telmate/proxmox", InfrastructureProviders.Proxmox, connection =>
            HasToken(connection)
                ?
                [
                    new("PM_API_URL", ConnectionField.ApiUrl),
                    new("PM_TLS_INSECURE", ConnectionField.Insecure),
                    new("PM_API_TOKEN_ID", ConnectionField.ApiTokenId),
                    new("PM_API_TOKEN_SECRET", ConnectionField.ApiTokenSecret)
                ]
                :
                [
                    new("PM_API_URL", ConnectionField.ApiUrl),
                    new("PM_TLS_INSECURE", ConnectionField.Insecure),
                    new("PM_USER", ConnectionField.Username),
                    new("PM_PASS", ConnectionField.Password)
                ]),

        new("hashicorp/vsphere", "hashicorp/vsphere", InfrastructureProviders.Vsphere, _ =>
        [
            new("VSPHERE_SERVER", ConnectionField.Host),
            new("VSPHERE_USER", ConnectionField.Username),
            new("VSPHERE_PASSWORD", ConnectionField.Password),
            new("VSPHERE_ALLOW_UNVERIFIED_SSL", ConnectionField.Insecure)
        ])
    ];

    /// <summary>The presets for a connection of <paramref name="provider"/>.</summary>
    public static IEnumerable<ConnectionPreset> For(string provider) =>
        All.Where(x => string.Equals(x.Provider, provider?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static ConnectionPreset Find(string id) =>
        All.FirstOrDefault(x => string.Equals(x.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase));

    private static bool HasToken(InfrastructureConnectionOptions connection) => !string.IsNullOrWhiteSpace(connection.ApiToken);
}
