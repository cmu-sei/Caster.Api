// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Infrastructure.Options;
using Microsoft.Extensions.Logging;

namespace Caster.Api.Domain.Services.Inventory;

/// <summary>
/// Reads inventory from whichever provider is configured. Kept behind an
/// interface so it can be faked in tests, since there is no provider available to
/// the test suite. The implementation registered in the container is
/// <see cref="InventoryProviderDispatcher"/>, not a single provider.
/// </summary>
public interface IInventoryProviderClient
{
    Task<InventorySnapshot> GetInventoryAsync(InfrastructureOptions options, CancellationToken cancellationToken);
}

/// <summary>
/// Read-only inventory reader for vCenter, using the vSphere Automation REST api.
/// Deliberately has no dependency beyond IHttpClientFactory, and never issues
/// anything but GET, POST /api/session and DELETE /api/session.
/// </summary>
public class VsphereInventoryClient(
    IHttpClientFactory httpClientFactory,
    ILogger<VsphereInventoryClient> logger) : IInventoryProvider
{
    public const string HttpClientName = "vsphere-inventory";

    string IInventoryProvider.Provider => InfrastructureProviders.Vsphere;

    string IInventoryProvider.HttpClientName => HttpClientName;

    private const string SessionHeader = "vmware-api-session-id";
    private const string SessionPath = "api/session";
    private const string VmPath = "api/vcenter/vm";
    private const string DatastorePath = "api/vcenter/datastore";
    private const string NetworkPath = "api/vcenter/network";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<InventorySnapshot> GetInventoryAsync(InfrastructureOptions options, CancellationToken cancellationToken)
    {
        // Provider selection lives in InventoryProviderDispatcher, so reaching
        // this method means vsphere was asked for.
        if (string.IsNullOrWhiteSpace(options.Url) ||
            string.IsNullOrWhiteSpace(options.Username) ||
            string.IsNullOrWhiteSpace(options.Password))
        {
            return InventorySnapshot.AllUnavailable(InventoryMessages.NotConfigured);
        }

        using var client = CreateClient(options);
        string sessionToken;

        try
        {
            sessionToken = await CreateSessionAsync(client, options, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var message = Redact($"Could not connect to vCenter at {options.Url}: {ex.Message}", options);
            logger.LogError(ex, "Could not create a vCenter session for {VsphereUrl} as {VsphereUsername}", options.Url, options.Username);
            return InventorySnapshot.AllUnavailable(message);
        }

        try
        {
            var snapshot = new InventorySnapshot
            {
                LastUpdated = DateTime.UtcNow,
                VmTemplates = await ReadVmTemplatesAsync(client, sessionToken, options, cancellationToken),
                Networks = await ReadNetworksAsync(client, sessionToken, options, cancellationToken),
                Datastores = await ReadDatastoresAsync(client, sessionToken, options, cancellationToken),
                // See InventoryMessages.IsoNotSupported / CRU-2828.
                Isos = InventoryCategoryResult.Unavailable(InventoryMessages.IsoNotSupported)
            };

            return snapshot;
        }
        finally
        {
            await DeleteSessionAsync(client, sessionToken, cancellationToken);
        }
    }

    private HttpClient CreateClient(InfrastructureOptions options)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress = new Uri(options.Url.TrimEnd('/') + "/", UriKind.Absolute);
        return client;
    }

    #region Session

    private static async Task<string> CreateSessionAsync(HttpClient client, InfrastructureOptions options, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, SessionPath);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(response.StatusCode == HttpStatusCode.Unauthorized
                ? "the credentials were rejected (401 Unauthorized)"
                : $"the server returned {(int)response.StatusCode} {response.StatusCode}");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var token = ParseSessionToken(body);

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new HttpRequestException("the session response did not contain a session id");
        }

        return token;
    }

    private async Task DeleteSessionAsync(HttpClient client, string sessionToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
            return;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, SessionPath);
            request.Headers.Add(SessionHeader, sessionToken);
            using var response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            // Session cleanup is best effort. vCenter expires idle sessions on its own.
            logger.LogDebug(ex, "Could not delete the vCenter inventory session");
        }
    }

    internal static string ParseSessionToken(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            var element = document.RootElement;

            // 7.0U2+ /api returns a bare json string, older /rest wraps it in { "value": ... }
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("value", out var value))
            {
                element = value;
            }

            return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
        }
        catch (JsonException)
        {
            return body.Trim().Trim('"');
        }
    }

    #endregion

    #region Categories

    private async Task<InventoryCategoryResult> ReadVmTemplatesAsync(
        HttpClient client, string sessionToken, InfrastructureOptions options, CancellationToken cancellationToken)
    {
        return await ReadCategoryAsync<VsphereVm>(client, sessionToken, VmPath, "VM templates", options, cancellationToken, vms =>
        {
            if (vms.Length == 0)
            {
                return InventoryCategoryResult.Ok([]);
            }

            // Not every vCenter build reports a template flag on the VM list. If
            // none of them do we cannot tell templates from running VMs, and
            // returning an empty list would look like "there are no templates".
            if (!vms.Any(x => x.Template.HasValue))
            {
                return InventoryCategoryResult.Unavailable(
                    $"vCenter returned {vms.Length} VMs but none reported a template flag, so VM templates cannot be identified through the Automation REST API on this vCenter build.");
            }

            var entries = vms
                .Where(x => x.Template == true)
                .Select(x =>
                {
                    var properties = new Dictionary<string, string>();
                    AddIfPresent(properties, "powerState", x.PowerState);
                    AddIfPresent(properties, "cpuCount", x.CpuCount?.ToString());
                    AddIfPresent(properties, "memorySizeMiB", x.MemorySizeMiB?.ToString());

                    return new InventoryEntry
                    {
                        Id = x.Vm,
                        Name = x.Name,
                        Path = x.Name,
                        Properties = properties
                    };
                })
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return InventoryCategoryResult.Ok(entries);
        });
    }

    private async Task<InventoryCategoryResult> ReadNetworksAsync(
        HttpClient client, string sessionToken, InfrastructureOptions options, CancellationToken cancellationToken)
    {
        return await ReadCategoryAsync<VsphereNetwork>(client, sessionToken, NetworkPath, "networks", options, cancellationToken, networks =>
        {
            var entries = networks
                .Select(x =>
                {
                    var properties = new Dictionary<string, string>();
                    AddIfPresent(properties, "type", x.Type);

                    return new InventoryEntry
                    {
                        Id = x.Network,
                        Name = x.Name,
                        Path = x.Name,
                        Properties = properties
                    };
                })
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return InventoryCategoryResult.Ok(entries);
        });
    }

    private async Task<InventoryCategoryResult> ReadDatastoresAsync(
        HttpClient client, string sessionToken, InfrastructureOptions options, CancellationToken cancellationToken)
    {
        return await ReadCategoryAsync<VsphereDatastore>(client, sessionToken, DatastorePath, "datastores", options, cancellationToken, datastores =>
        {
            var entries = datastores
                .Select(x =>
                {
                    var properties = new Dictionary<string, string>();
                    AddIfPresent(properties, "type", x.Type);
                    AddIfPresent(properties, "capacity", x.Capacity?.ToString());
                    AddIfPresent(properties, "freeSpace", x.FreeSpace?.ToString());

                    return new InventoryEntry
                    {
                        Id = x.Datastore,
                        Name = x.Name,
                        Path = x.Name,
                        Properties = properties
                    };
                })
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return InventoryCategoryResult.Ok(entries);
        });
    }

    private async Task<InventoryCategoryResult> ReadCategoryAsync<T>(
        HttpClient client,
        string sessionToken,
        string path,
        string description,
        InfrastructureOptions options,
        CancellationToken cancellationToken,
        Func<T[], InventoryCategoryResult> project)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add(SessionHeader, sessionToken);

            using var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return InventoryCategoryResult.Unavailable(
                    $"vCenter returned {(int)response.StatusCode} {response.StatusCode} when reading {description}.");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var values = DeserializeList<T>(body);

            logger.LogInformation("Read {Count} raw {Description} from {VsphereUrl}", values.Length, description, options.Url);

            return project(values);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Exception reading {Description} from {VsphereUrl}", description, options.Url);
            return InventoryCategoryResult.Unavailable(Redact($"Could not read {description} from vCenter: {ex.Message}", options));
        }
    }

    internal static T[] DeserializeList<T>(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return [];

        using var document = JsonDocument.Parse(body);
        var element = document.RootElement;

        // 7.0U2+ /api returns a bare array, older /rest wraps it in { "value": [...] }
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("value", out var value))
        {
            element = value;
        }

        if (element.ValueKind != JsonValueKind.Array)
            return [];

        return element.Deserialize<T[]>(JsonOptions) ?? [];
    }

    private static void AddIfPresent(Dictionary<string, string> properties, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            properties[key] = value;
        }
    }

    private static string Redact(string message, InfrastructureOptions options) =>
        InventoryRedaction.Redact(message, options);

    #endregion

    #region vSphere response shapes

    private class VsphereVm
    {
        [JsonPropertyName("vm")]
        public string Vm { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("power_state")]
        public string PowerState { get; set; }

        [JsonPropertyName("cpu_count")]
        public int? CpuCount { get; set; }

        [JsonPropertyName("memory_size_MiB")]
        public long? MemorySizeMiB { get; set; }

        [JsonPropertyName("template")]
        public bool? Template { get; set; }
    }

    private class VsphereNetwork
    {
        [JsonPropertyName("network")]
        public string Network { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }
    }

    private class VsphereDatastore
    {
        [JsonPropertyName("datastore")]
        public string Datastore { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("free_space")]
        public long? FreeSpace { get; set; }

        [JsonPropertyName("capacity")]
        public long? Capacity { get; set; }
    }

    #endregion
}
