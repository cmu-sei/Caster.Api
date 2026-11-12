// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Options;

namespace Caster.Api.Domain.Services.EnvironmentSets;

/// <summary>A connection field's value, or why it has none. Never both.</summary>
public record ConnectionFieldValue(string Value, string Unavailable)
{
    public bool IsAvailable => Unavailable is null;

    public static ConnectionFieldValue Of(string value) => new(value, null);

    public static ConnectionFieldValue Missing(string why) => new(null, why);
}

/// <summary>
/// What each <see cref="ConnectionField"/> is for a connection. Worked out from Caster's own
/// connection settings, so a new form a provider wants is a new field, not a template.
/// </summary>
public static class ConnectionFields
{
    private const string ProxmoxApiPath = "/api2/json";

    public static readonly ConnectionField[] All = Enum.GetValues<ConnectionField>();

    /// <summary>Whether the field is a credential: masked in output, and never a Terraform variable.</summary>
    public static bool IsSecret(ConnectionField field) => field is
        ConnectionField.Password or ConnectionField.ApiToken or ConnectionField.ApiTokenId or ConnectionField.ApiTokenSecret;

    /// <summary>Whether a connection of <paramref name="provider"/> can ever have the field.</summary>
    public static bool AppliesTo(ConnectionField field, string provider)
    {
        var proxmox = IsProvider(provider, InfrastructureProviders.Proxmox);
        var vsphere = IsProvider(provider, InfrastructureProviders.Vsphere);

        return field switch
        {
            ConnectionField.ApiUrl or ConnectionField.ApiToken or ConnectionField.ApiTokenId or ConnectionField.ApiTokenSecret => proxmox,
            ConnectionField.IsoFolder or ConnectionField.Datacenter => vsphere,
            _ => proxmox || vsphere
        };
    }

    /// <summary>The fields a connection of <paramref name="provider"/> can have.</summary>
    public static ConnectionField[] For(string provider) => All.Where(x => AppliesTo(x, provider)).ToArray();

    /// <summary>The field's value for <paramref name="connection"/>, or why it has none.</summary>
    public static ConnectionFieldValue Resolve(InfrastructureConnectionOptions connection, ConnectionField field)
    {
        if (!AppliesTo(field, connection.Provider))
            return ConnectionFieldValue.Missing($"{field} doesn't apply to {connection.Provider} connections.");

        return field switch
        {
            ConnectionField.Url => Required(connection.Url?.Trim(), "Url"),
            ConnectionField.Host => Host(connection.Url),
            ConnectionField.ApiUrl => ApiUrl(connection.Url),
            ConnectionField.Insecure => ConnectionFieldValue.Of(connection.InsecureSkipVerify ? "true" : "false"),
            ConnectionField.Username => Required(connection.Username, "Username"),
            ConnectionField.Password => Required(connection.Password, "Password"),
            ConnectionField.ApiToken => Required(connection.ApiToken, "ApiToken"),
            ConnectionField.ApiTokenId => TokenPart(connection.ApiToken, id: true),
            ConnectionField.ApiTokenSecret => TokenPart(connection.ApiToken, id: false),
            ConnectionField.IsoStorage => IsoLocation(connection, folder: false),
            ConnectionField.IsoFolder => IsoLocation(connection, folder: true),
            ConnectionField.Datacenter => Only(connection.Datacenters, "Datacenters"),
            _ => ConnectionFieldValue.Missing($"{field} is not a connection field.")
        };
    }

    private static bool IsProvider(string provider, string wanted) =>
        string.Equals(provider?.Trim(), wanted, StringComparison.OrdinalIgnoreCase);

    private static ConnectionFieldValue Required(string value, string setting) =>
        string.IsNullOrEmpty(value)
            ? ConnectionFieldValue.Missing($"the connection has no {setting}.")
            : ConnectionFieldValue.Of(value);

    private static ConnectionFieldValue Host(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return ConnectionFieldValue.Missing("the connection has no Url.");

        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
            ? ConnectionFieldValue.Of(uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}")
            : ConnectionFieldValue.Missing("the connection's Url is not an absolute url.");
    }

    private static ConnectionFieldValue ApiUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return ConnectionFieldValue.Missing("the connection has no Url.");

        var trimmed = url.Trim().TrimEnd('/');

        return ConnectionFieldValue.Of(trimmed.EndsWith(ProxmoxApiPath, StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + ProxmoxApiPath);
    }

    private static ConnectionFieldValue TokenPart(string token, bool id)
    {
        if (string.IsNullOrEmpty(token))
            return ConnectionFieldValue.Missing("the connection has no ApiToken.");

        var separator = token.IndexOf('=');

        if (separator <= 0 || separator == token.Length - 1)
            return ConnectionFieldValue.Missing("the connection's ApiToken is not of the form user@realm!id=secret.");

        return ConnectionFieldValue.Of(id ? token[..separator] : token[(separator + 1)..]);
    }

    private static ConnectionFieldValue Only(string[] values, string setting)
    {
        var entries = (values ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray();

        return entries.Length switch
        {
            1 => ConnectionFieldValue.Of(entries[0]),
            0 => ConnectionFieldValue.Missing($"the connection has no {setting} entry."),
            _ => ConnectionFieldValue.Missing($"the connection has {entries.Length} {setting} entries, so which one is meant is ambiguous.")
        };
    }

    /// <summary>
    /// vSphere: "[ds] folder" is datastore ds and folder "folder"; "ds" is datastore ds and no
    /// folder. Proxmox: "storage" or a volid prefix "storage:iso/..." is storage "storage".
    /// </summary>
    private static ConnectionFieldValue IsoLocation(InfrastructureConnectionOptions connection, bool folder)
    {
        var only = Only(connection.IsoLocations, "IsoLocations");

        if (!only.IsAvailable)
            return only;

        var entry = only.Value;

        if (IsProvider(connection.Provider, InfrastructureProviders.Proxmox))
        {
            var colon = entry.IndexOf(':');
            var storage = colon > 0 ? entry[..colon] : entry;

            return storage.Contains('/') || colon == 0
                ? ConnectionFieldValue.Missing("the connection's IsoLocations entry is not a storage name or volid prefix.")
                : ConnectionFieldValue.Of(storage);
        }

        if (entry.StartsWith('[') && entry.IndexOf(']') is var close and > 1)
        {
            return ConnectionFieldValue.Of(folder
                ? entry[(close + 1)..].Trim().Trim('/')
                : entry[1..close]);
        }

        return ConnectionFieldValue.Of(folder ? string.Empty : entry);
    }
}
