// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Infrastructure.Options;
using Microsoft.Extensions.Logging;

namespace Caster.Api.Domain.Services.Inventory;

/// <summary>
/// Read-only inventory reader for Proxmox VE, using the <c>/api2/json</c> rest
/// api. Like the vSphere reader it depends on nothing but IHttpClientFactory, and
/// issues nothing but GET plus the one optional POST /access/ticket login.
/// <para>
/// Unlike vSphere this provider can enumerate ISOs, because Proxmox exposes
/// storage content listings over rest - see CRU-2828.
/// </para>
/// </summary>
public class ProxmoxInventoryClient(
    IHttpClientFactory httpClientFactory,
    ILogger<ProxmoxInventoryClient> logger) : IInventoryProvider
{
    public const string HttpClientName = "proxmox-inventory";

    /// <summary>
    /// Appended to Infrastructure:Url when it is not already present.
    /// </summary>
    private const string ApiRoot = "/api2/json";

    private const string TicketPath = "access/ticket";
    private const string VersionPath = "version";
    private const string VmResourcesPath = "cluster/resources?type=vm";
    private const string StoragePath = "storage";
    private const string NodesPath = "nodes";
    private const string SdnVnetsPath = "cluster/sdn/vnets";

    private const string IsoContent = "iso";
    private const string BridgeType = "bridge";
    private const string VnetType = "vnet";

    private const string AuthorizationHeader = "Authorization";
    private const string CookieHeader = "Cookie";
    private const string CsrfHeader = "CSRFPreventionToken";
    private const string TokenScheme = "PVEAPIToken";
    private const string TicketCookieName = "PVEAuthCookie";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    string IInventoryProvider.Provider => InfrastructureProviders.Proxmox;

    string IInventoryProvider.HttpClientName => HttpClientName;

    public async Task<InventorySnapshot> GetInventoryAsync(InfrastructureOptions options, CancellationToken cancellationToken)
    {
        // Provider selection lives in InventoryProviderDispatcher, so reaching
        // this method means proxmox was asked for.
        var hasToken = !string.IsNullOrWhiteSpace(options.ApiToken);
        var hasUserPassword = !string.IsNullOrWhiteSpace(options.Username) && !string.IsNullOrWhiteSpace(options.Password);

        if (string.IsNullOrWhiteSpace(options.Url) || (!hasToken && !hasUserPassword))
        {
            return InventorySnapshot.AllUnavailable(InventoryMessages.ProxmoxNotConfigured);
        }

        if (hasToken && !IsWellFormedApiToken(options.ApiToken))
        {
            return InventorySnapshot.AllUnavailable(InventoryMessages.ProxmoxApiTokenMalformed);
        }

        if (!Uri.TryCreate(BuildBaseUrl(options.Url), UriKind.Absolute, out var baseAddress))
        {
            return InventorySnapshot.AllUnavailable(
                "Infrastructure:Url is not a valid absolute url, so the Proxmox api address could not be built.");
        }

        using var client = httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress = baseAddress;

        ProxmoxCredential credential;

        try
        {
            credential = hasToken
                ? ProxmoxCredential.FromApiToken(options.ApiToken)
                : await CreateTicketAsync(client, options, cancellationToken);

            // Both auth modes are verified the same way, so rejected credentials
            // fail once here instead of four times inside the categories.
            await VerifyAsync(client, credential, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var message = Redact($"Could not connect to Proxmox at {options.Url}: {ex.Message}", options);
            logger.LogError(ex, "Could not authenticate to Proxmox at {ProxmoxUrl}", options.Url);
            return InventorySnapshot.AllUnavailable(message);
        }

        logger.LogInformation("Authenticated to Proxmox at {ProxmoxUrl} using {ProxmoxAuthMode}", options.Url, credential.Mode);

        // Nodes and storages are each read once and shared, because three of the
        // four categories need them. A failure still only disables the categories
        // that actually depend on the failed read.
        var storages = await ReadAsync<ProxmoxStorage>(client, credential, StoragePath, "storages", options, cancellationToken);
        var nodes = await ReadAsync<ProxmoxNode>(client, credential, NodesPath, "nodes", options, cancellationToken);

        return new InventorySnapshot
        {
            LastUpdated = DateTime.UtcNow,
            VmTemplates = await ReadVmTemplatesAsync(client, credential, options, cancellationToken),
            Isos = await ReadIsosAsync(client, credential, nodes, storages, options, cancellationToken),
            Networks = await ReadNetworksAsync(client, credential, nodes, options, cancellationToken),
            Datastores = ProjectDatastores(storages)
        };
    }

    /// <summary>
    /// Turns Infrastructure:Url into the api base, e.g.
    /// <c>https://pve.example.com:8006</c> becomes
    /// <c>https://pve.example.com:8006/api2/json/</c>. The port is never guessed,
    /// so a Proxmox behind a reverse proxy on 443 works unchanged.
    /// </summary>
    public static string BuildBaseUrl(string url)
    {
        var trimmed = (url ?? string.Empty).Trim().TrimEnd('/');

        if (!trimmed.EndsWith(ApiRoot, StringComparison.OrdinalIgnoreCase))
        {
            trimmed += ApiRoot;
        }

        return trimmed + "/";
    }

    /// <summary>
    /// Checks the shape of <see cref="InfrastructureOptions.ApiToken"/> without
    /// ever copying any part of it into a message:
    /// <c>user@realm!tokenid=secret</c>.
    /// </summary>
    internal static bool IsWellFormedApiToken(string apiToken)
    {
        if (string.IsNullOrWhiteSpace(apiToken))
            return false;

        var token = apiToken.Trim();
        var at = token.IndexOf('@');
        var bang = token.IndexOf('!');
        var equals = token.IndexOf('=');

        return at > 0 &&
            bang > at + 1 &&
            equals > bang + 1 &&
            equals < token.Length - 1;
    }

    #region Authentication

    /// <summary>
    /// Username/password fallback. Reads only need the ticket cookie, but the
    /// csrf token is kept and sent so the credential works unchanged if a future
    /// caller ever needs a write.
    /// </summary>
    private static async Task<ProxmoxCredential> CreateTicketAsync(
        HttpClient client, InfrastructureOptions options, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TicketPath)
        {
            Content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("username", options.Username),
                new KeyValuePair<string, string>("password", options.Password)
            ])
        };

        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(DescribeStatus(response.StatusCode));
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var (ticket, csrfToken) = ParseTicket(body);

        if (string.IsNullOrWhiteSpace(ticket))
        {
            throw new HttpRequestException("the login response did not contain a ticket");
        }

        return ProxmoxCredential.FromTicket(ticket, csrfToken);
    }

    internal static (string Ticket, string CsrfToken) ParseTicket(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return (null, null);

        try
        {
            using var document = JsonDocument.Parse(body);
            var element = document.RootElement;

            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("data", out var data))
            {
                element = data;
            }

            if (element.ValueKind != JsonValueKind.Object)
                return (null, null);

            var ticket = element.TryGetProperty("ticket", out var ticketElement) && ticketElement.ValueKind == JsonValueKind.String
                ? ticketElement.GetString()
                : null;

            var csrfToken = element.TryGetProperty(CsrfHeader, out var csrfElement) && csrfElement.ValueKind == JsonValueKind.String
                ? csrfElement.GetString()
                : null;

            return (ticket, csrfToken);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    /// <summary>
    /// Cheapest authenticated read Proxmox offers. An api token is never
    /// exchanged for anything, so without this a bad token would surface as four
    /// separate 401s instead of one "credentials were rejected".
    /// </summary>
    private static async Task VerifyAsync(HttpClient client, ProxmoxCredential credential, CancellationToken cancellationToken)
    {
        using var request = NewRequest(HttpMethod.Get, VersionPath, credential);
        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(DescribeStatus(response.StatusCode));
        }
    }

    private static string DescribeStatus(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => "the credentials were rejected (401 Unauthorized)",
        HttpStatusCode.Forbidden => "the credentials were accepted but lack permission (403 Forbidden)",
        _ => $"the server returned {(int)statusCode} {statusCode}"
    };

    private static HttpRequestMessage NewRequest(HttpMethod method, string path, ProxmoxCredential credential)
    {
        var request = new HttpRequestMessage(method, path);

        // PVEAPIToken=user@realm!id=secret is not scheme + space + parameter, so
        // it cannot go through AuthenticationHeaderValue.
        if (!string.IsNullOrEmpty(credential.Authorization))
        {
            request.Headers.TryAddWithoutValidation(AuthorizationHeader, credential.Authorization);
        }

        if (!string.IsNullOrEmpty(credential.Cookie))
        {
            request.Headers.TryAddWithoutValidation(CookieHeader, credential.Cookie);
        }

        if (!string.IsNullOrEmpty(credential.CsrfToken))
        {
            request.Headers.TryAddWithoutValidation(CsrfHeader, credential.CsrfToken);
        }

        return request;
    }

    #endregion

    #region Categories

    /// <summary>
    /// GET /cluster/resources?type=vm, keeping the entries whose integer
    /// <c>template</c> flag is 1. Confirmed against PVE 9.1.1, where every qemu
    /// and lxc entry carries the flag.
    /// </summary>
    private async Task<InventoryCategoryResult> ReadVmTemplatesAsync(
        HttpClient client, ProxmoxCredential credential, InfrastructureOptions options, CancellationToken cancellationToken)
    {
        var read = await ReadAsync<ProxmoxResource>(client, credential, VmResourcesPath, "VM templates", options, cancellationToken);

        if (!read.Ok)
            return InventoryCategoryResult.Unavailable(read.Error);

        var entries = read.Values
            .Where(x => x.Template == true)
            .Select(x =>
            {
                var properties = new Dictionary<string, string>();
                AddIfPresent(properties, "vmid", x.VmId?.ToString(CultureInfo.InvariantCulture));
                AddIfPresent(properties, "node", x.Node);
                AddIfPresent(properties, "type", x.Type);
                AddIfPresent(properties, "status", x.Status);
                AddIfPresent(properties, "maxcpu", x.MaxCpu?.ToString(CultureInfo.InvariantCulture));
                AddIfPresent(properties, "maxmem", x.MaxMem?.ToString(CultureInfo.InvariantCulture));
                AddIfPresent(properties, "maxdisk", x.MaxDisk?.ToString(CultureInfo.InvariantCulture));

                return new InventoryEntry
                {
                    // "qemu/105" - the only stable native identifier Proxmox gives a VM here.
                    Id = x.Id,
                    Name = x.Name,
                    // Proxmox has no inventory path for a VM, so follow the
                    // vSphere reader and repeat the name rather than fabricate one.
                    Path = x.Name,
                    Properties = properties
                };
            })
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return InventoryCategoryResult.Ok(entries);
    }

    /// <summary>
    /// GET /storage. Also the source of the iso-capable storage list used by
    /// <see cref="ReadIsosAsync"/>.
    /// </summary>
    private static InventoryCategoryResult ProjectDatastores(ProviderRead<ProxmoxStorage> storages)
    {
        if (!storages.Ok)
            return InventoryCategoryResult.Unavailable(storages.Error);

        var entries = storages.Values
            .Where(x => !string.IsNullOrWhiteSpace(x.Storage))
            .Select(x =>
            {
                var properties = new Dictionary<string, string>();
                AddIfPresent(properties, "type", x.Type);
                AddIfPresent(properties, "content", x.Content);
                AddIfPresent(properties, "path", x.Path);

                return new InventoryEntry
                {
                    // Proxmox identifies a storage by its name, cluster wide.
                    Id = x.Storage,
                    Name = x.Storage,
                    Path = x.Storage,
                    Properties = properties
                };
            })
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return InventoryCategoryResult.Ok(entries);
    }

    /// <summary>
    /// GET /nodes/{node}/storage/{storage}/content?content=iso for every
    /// iso-capable storage on every queryable node. The volid
    /// ("storage:iso/file.iso") is already the fully qualified value a terraform
    /// module wants, so it is both the id and the name.
    /// <para>
    /// A single failed storage read makes the whole category unavailable rather
    /// than returning a short list that would read as complete, which is the same
    /// rule the vSphere reader applies to the VM template flag.
    /// </para>
    /// </summary>
    private async Task<InventoryCategoryResult> ReadIsosAsync(
        HttpClient client,
        ProxmoxCredential credential,
        ProviderRead<ProxmoxNode> nodes,
        ProviderRead<ProxmoxStorage> storages,
        InfrastructureOptions options,
        CancellationToken cancellationToken)
    {
        if (!nodes.Ok)
            return InventoryCategoryResult.Unavailable($"ISO discovery needs the Proxmox node list. {nodes.Error}");

        if (!storages.Ok)
            return InventoryCategoryResult.Unavailable($"ISO discovery needs the Proxmox storage list. {storages.Error}");

        var isoStorages = storages.Values
            .Where(x => !string.IsNullOrWhiteSpace(x.Storage) && HasContent(x.Content, IsoContent))
            .ToArray();

        var queryableNodes = QueryableNodes(nodes.Values);

        if (isoStorages.Length == 0 || queryableNodes.Length == 0)
            return InventoryCategoryResult.Ok([]);

        // Shared storage is listed once per node, so dedupe on volid.
        var entries = new Dictionary<string, InventoryEntry>(StringComparer.Ordinal);
        var failures = new List<string>();

        foreach (var node in queryableNodes)
        {
            foreach (var storage in isoStorages)
            {
                var path = $"nodes/{Uri.EscapeDataString(node)}/storage/{Uri.EscapeDataString(storage.Storage)}/content?content={IsoContent}";
                var description = $"ISOs on {node}/{storage.Storage}";
                var read = await ReadAsync<ProxmoxStorageContent>(client, credential, path, description, options, cancellationToken);

                if (!read.Ok)
                {
                    failures.Add(read.Error);
                    continue;
                }

                foreach (var item in read.Values)
                {
                    if (string.IsNullOrWhiteSpace(item.VolId) || entries.ContainsKey(item.VolId))
                        continue;

                    var properties = new Dictionary<string, string>();
                    AddIfPresent(properties, "storage", storage.Storage);
                    AddIfPresent(properties, "node", node);
                    AddIfPresent(properties, "format", item.Format);
                    AddIfPresent(properties, "size", item.Size?.ToString(CultureInfo.InvariantCulture));
                    AddIfPresent(properties, "created", FromUnixSeconds(item.CTime));

                    entries[item.VolId] = new InventoryEntry
                    {
                        Id = item.VolId,
                        Name = item.VolId,
                        // Already fully qualified as storage:iso/filename.
                        Path = item.VolId,
                        Properties = properties
                    };
                }
            }
        }

        if (failures.Count > 0)
        {
            return InventoryCategoryResult.Unavailable(
                $"{failures.Count} of {queryableNodes.Length * isoStorages.Length} Proxmox ISO storage reads failed, so the ISO list would be incomplete. First failure: {failures[0]}");
        }

        return InventoryCategoryResult.Ok(Ordered(entries.Values));
    }

    /// <summary>
    /// GET /nodes/{node}/network filtered to <c>type == "bridge"</c>, plus
    /// GET /cluster/sdn/vnets. A bridge of the same name on several nodes is one
    /// usable network to a terraform module, so they are deduped by name and the
    /// nodes it exists on are reported in a property.
    /// <para>
    /// SDN vnets also materialise as bridges on each node, so a vnet that matches
    /// an already discovered bridge enriches that entry instead of duplicating it.
    /// </para>
    /// </summary>
    private async Task<InventoryCategoryResult> ReadNetworksAsync(
        HttpClient client,
        ProxmoxCredential credential,
        ProviderRead<ProxmoxNode> nodes,
        InfrastructureOptions options,
        CancellationToken cancellationToken)
    {
        if (!nodes.Ok)
            return InventoryCategoryResult.Unavailable($"Network discovery needs the Proxmox node list. {nodes.Error}");

        var queryableNodes = QueryableNodes(nodes.Values);
        var entries = new Dictionary<string, InventoryEntry>(StringComparer.Ordinal);
        var nodesByInterface = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var failures = new List<string>();

        foreach (var node in queryableNodes)
        {
            var read = await ReadAsync<ProxmoxNetworkInterface>(
                client, credential, $"nodes/{Uri.EscapeDataString(node)}/network", $"networks on {node}", options, cancellationToken);

            if (!read.Ok)
            {
                failures.Add(read.Error);
                continue;
            }

            foreach (var iface in read.Values)
            {
                if (string.IsNullOrWhiteSpace(iface.Iface) ||
                    !string.Equals(iface.Type, BridgeType, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!nodesByInterface.TryGetValue(iface.Iface, out var owners))
                {
                    owners = [];
                    nodesByInterface[iface.Iface] = owners;
                }

                owners.Add(node);

                if (entries.ContainsKey(iface.Iface))
                    continue;

                var properties = new Dictionary<string, string>();
                AddIfPresent(properties, "type", iface.Type);
                AddIfPresent(properties, "cidr", iface.Cidr);
                AddIfPresent(properties, "bridgePorts", iface.BridgePorts);
                AddIfPresent(properties, "active", iface.Active?.ToString(CultureInfo.InvariantCulture));

                entries[iface.Iface] = new InventoryEntry
                {
                    // Proxmox has no object id for an interface; the name is what
                    // a terraform module references, cluster wide.
                    Id = iface.Iface,
                    Name = iface.Iface,
                    Path = iface.Iface,
                    Properties = properties
                };
            }
        }

        if (failures.Count > 0)
        {
            return InventoryCategoryResult.Unavailable(
                $"{failures.Count} of {queryableNodes.Length} Proxmox node network reads failed, so the network list would be incomplete. First failure: {failures[0]}");
        }

        foreach (var (iface, owners) in nodesByInterface)
        {
            entries[iface].Properties["nodes"] = string.Join(",", owners);
        }

        await AddSdnVnetsAsync(client, credential, entries, options, cancellationToken);

        return InventoryCategoryResult.Ok(Ordered(entries.Values));
    }

    /// <summary>
    /// SDN is optional and returns an empty list when unconfigured, so a failure
    /// here is logged and ignored rather than failing the network category - an
    /// older or SDN-less Proxmox must still report its bridges.
    /// </summary>
    private async Task AddSdnVnetsAsync(
        HttpClient client,
        ProxmoxCredential credential,
        Dictionary<string, InventoryEntry> entries,
        InfrastructureOptions options,
        CancellationToken cancellationToken)
    {
        var read = await ReadAsync<ProxmoxVnet>(client, credential, SdnVnetsPath, "SDN vnets", options, cancellationToken);

        if (!read.Ok)
        {
            logger.LogDebug("Could not read Proxmox SDN vnets, continuing with node bridges only: {ProxmoxSdnError}", read.Error);
            return;
        }

        foreach (var vnet in read.Values)
        {
            if (string.IsNullOrWhiteSpace(vnet.Vnet))
                continue;

            // A vnet is realised as a bridge on each node, so it has very likely
            // already been discovered under the same name.
            if (!entries.TryGetValue(vnet.Vnet, out var entry))
            {
                entry = new InventoryEntry
                {
                    Id = vnet.Vnet,
                    Name = vnet.Vnet,
                    Path = vnet.Vnet,
                    Properties = []
                };

                entries[vnet.Vnet] = entry;
                AddIfPresent(entry.Properties, "type", string.IsNullOrWhiteSpace(vnet.Type) ? VnetType : vnet.Type);
            }

            AddIfPresent(entry.Properties, "zone", vnet.Zone);
            AddIfPresent(entry.Properties, "alias", vnet.Alias);
            AddIfPresent(entry.Properties, "tag", vnet.Tag?.ToString(CultureInfo.InvariantCulture));
        }
    }

    #endregion

    #region Reads

    /// <summary>
    /// The single GET path. Never throws: a failure becomes an
    /// <see cref="ProviderRead{T}.Error"/> the calling category decides what to
    /// do with, which is what keeps the categories independent.
    /// </summary>
    private async Task<ProviderRead<T>> ReadAsync<T>(
        HttpClient client,
        ProxmoxCredential credential,
        string path,
        string description,
        InfrastructureOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = NewRequest(HttpMethod.Get, path, credential);
            using var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return ProviderRead<T>.Failed(
                    $"Proxmox returned {(int)response.StatusCode} {response.StatusCode} when reading {description}.");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var values = DeserializeList<T>(body);

            logger.LogInformation("Read {Count} raw {Description} from {ProxmoxUrl}", values.Length, description, options.Url);

            return ProviderRead<T>.Succeeded(values);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Exception reading {Description} from {ProxmoxUrl}", description, options.Url);

            return ProviderRead<T>.Failed(
                Redact($"Could not read {description} from Proxmox: {ex.Message}", options, credential?.Ticket));
        }
    }

    /// <summary>
    /// Proxmox wraps every payload in a single "data" property.
    /// </summary>
    internal static T[] DeserializeList<T>(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return [];

        using var document = JsonDocument.Parse(body);
        var element = document.RootElement;

        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("data", out var data))
        {
            element = data;
        }

        if (element.ValueKind != JsonValueKind.Array)
            return [];

        return element.Deserialize<T[]>(JsonOptions) ?? [];
    }

    #endregion

    #region Helpers

    /// <summary>
    /// An offline node cannot answer, and that is expected rather than an error,
    /// so it is skipped instead of failing a category for the whole cluster.
    /// </summary>
    private static string[] QueryableNodes(ProxmoxNode[] nodes) => nodes
        .Where(x => !string.IsNullOrWhiteSpace(x.Node) &&
            !string.Equals(x.Status, "offline", StringComparison.OrdinalIgnoreCase))
        .Select(x => x.Node)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>
    /// Proxmox storage "content" is a comma separated list, e.g. "iso,backup,vztmpl".
    /// </summary>
    internal static bool HasContent(string content, string wanted)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        return content
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(x => string.Equals(x, wanted, StringComparison.OrdinalIgnoreCase));
    }

    private static string FromUnixSeconds(long? seconds) => seconds.HasValue
        ? DateTimeOffset.FromUnixTimeSeconds(seconds.Value).UtcDateTime.ToString("o", CultureInfo.InvariantCulture)
        : null;

    private static InventoryEntry[] Ordered(IEnumerable<InventoryEntry> entries) => entries
        .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static void AddIfPresent(Dictionary<string, string> properties, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            properties[key] = value;
        }
    }

    private static string Redact(string message, InfrastructureOptions options, params string[] extraSecrets) =>
        InventoryRedaction.Redact(message, options, extraSecrets);

    #endregion

    #region Internals

    /// <summary>
    /// Outcome of one GET. Error is null on success.
    /// </summary>
    private sealed class ProviderRead<T>
    {
        public T[] Values { get; private init; } = [];

        public string Error { get; private init; }

        public bool Ok => Error is null;

        public static ProviderRead<T> Succeeded(T[] values) => new() { Values = values ?? [] };

        public static ProviderRead<T> Failed(string error) => new() { Error = error };
    }

    /// <summary>
    /// Whichever of the two auth modes is in use, reduced to the headers a read
    /// needs.
    /// </summary>
    private sealed class ProxmoxCredential
    {
        public string Authorization { get; private init; }

        public string Cookie { get; private init; }

        public string CsrfToken { get; private init; }

        /// <summary>
        /// Held only so a ticket echoed back in an error string can be redacted.
        /// </summary>
        public string Ticket { get; private init; }

        public string Mode { get; private init; }

        public static ProxmoxCredential FromApiToken(string apiToken) => new()
        {
            Authorization = $"{TokenScheme}={apiToken.Trim()}",
            Mode = "an api token"
        };

        public static ProxmoxCredential FromTicket(string ticket, string csrfToken) => new()
        {
            Cookie = $"{TicketCookieName}={Uri.EscapeDataString(ticket)}",
            CsrfToken = csrfToken,
            Ticket = ticket,
            Mode = "a username/password ticket"
        };
    }

    #endregion

    #region Proxmox response shapes

    /// <summary>
    /// An entry of GET /cluster/resources?type=vm.
    /// </summary>
    private class ProxmoxResource
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("vmid")]
        public long? VmId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("node")]
        public string Node { get; set; }

        /// <summary>
        /// "qemu" or "lxc".
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; }

        /// <summary>
        /// Reported as the integer 0 or 1, not a json bool.
        /// </summary>
        [JsonPropertyName("template")]
        [JsonConverter(typeof(ProxmoxFlagConverter))]
        public bool? Template { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("maxcpu")]
        public long? MaxCpu { get; set; }

        [JsonPropertyName("maxmem")]
        public long? MaxMem { get; set; }

        [JsonPropertyName("maxdisk")]
        public long? MaxDisk { get; set; }
    }

    /// <summary>
    /// An entry of GET /storage.
    /// </summary>
    private class ProxmoxStorage
    {
        [JsonPropertyName("storage")]
        public string Storage { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        /// <summary>
        /// Comma separated, e.g. "vztmpl,backup,iso,snippets".
        /// </summary>
        [JsonPropertyName("content")]
        public string Content { get; set; }

        [JsonPropertyName("path")]
        public string Path { get; set; }
    }

    /// <summary>
    /// An entry of GET /nodes.
    /// </summary>
    private class ProxmoxNode
    {
        [JsonPropertyName("node")]
        public string Node { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }
    }

    /// <summary>
    /// An entry of GET /nodes/{node}/storage/{storage}/content?content=iso.
    /// </summary>
    private class ProxmoxStorageContent
    {
        /// <summary>
        /// "storage:iso/filename.iso".
        /// </summary>
        [JsonPropertyName("volid")]
        public string VolId { get; set; }

        [JsonPropertyName("format")]
        public string Format { get; set; }

        [JsonPropertyName("size")]
        public long? Size { get; set; }

        [JsonPropertyName("ctime")]
        public long? CTime { get; set; }
    }

    /// <summary>
    /// An entry of GET /nodes/{node}/network.
    /// </summary>
    private class ProxmoxNetworkInterface
    {
        [JsonPropertyName("iface")]
        public string Iface { get; set; }

        /// <summary>
        /// "bridge", "eth", "bond", "vlan" ...
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("cidr")]
        public string Cidr { get; set; }

        [JsonPropertyName("bridge_ports")]
        public string BridgePorts { get; set; }

        [JsonPropertyName("active")]
        public int? Active { get; set; }
    }

    /// <summary>
    /// An entry of GET /cluster/sdn/vnets. Empty on a host without SDN
    /// configured, which is the case on the Proxmox this was verified against.
    /// </summary>
    private class ProxmoxVnet
    {
        [JsonPropertyName("vnet")]
        public string Vnet { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("zone")]
        public string Zone { get; set; }

        [JsonPropertyName("alias")]
        public string Alias { get; set; }

        [JsonPropertyName("tag")]
        public long? Tag { get; set; }
    }

    /// <summary>
    /// Proxmox boolean flags come back as 0/1 integers. Tolerates a json bool or
    /// a quoted "0"/"1" too, so one api variation cannot fail a whole category.
    /// </summary>
    private sealed class ProxmoxFlagConverter : JsonConverter<bool?>
    {
        public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.True:
                    return true;
                case JsonTokenType.False:
                    return false;
                case JsonTokenType.Null:
                    return null;
                case JsonTokenType.Number:
                    return reader.TryGetInt64(out var number) ? number != 0 : null;
                case JsonTokenType.String:
                    var text = reader.GetString();
                    return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                        ? parsed != 0
                        : bool.TryParse(text, out var flag) ? flag : null;
                default:
                    return null;
            }
        }

        public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
        {
            if (value.HasValue)
            {
                writer.WriteNumberValue(value.Value ? 1 : 0);
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }

    #endregion
}
