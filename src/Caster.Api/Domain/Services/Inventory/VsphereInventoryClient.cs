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
    Task<InventorySnapshot> GetInventoryAsync(InfrastructureConnectionOptions options, CancellationToken cancellationToken);
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
    private const string DatacenterPath = "api/vcenter/datacenter";
    private const string FolderPath = "api/vcenter/folder";
    private const string LibraryPath = "api/content/library";
    private const string LibraryItemPath = "api/content/library/item";

    /// <summary>
    /// The folder walk costs two requests per folder, so it stops here. Templates
    /// it did not reach keep their bare name as their path.
    /// </summary>
    internal const int MaxFolders = 500;

    /// <summary>
    /// Each content library item costs a request. Past this the category reports
    /// itself unavailable rather than list some of the templates, since a partial
    /// list would let a disk manifest call a template missing.
    /// </summary>
    internal const int MaxLibraryItems = 2000;

    /// <summary>
    /// Content library item types that are VM templates: "vm-template" clones like
    /// an inventory template, "ovf" deploys as one. ISOs and other files are not.
    /// </summary>
    private static readonly HashSet<string> TemplateItemTypes = new(StringComparer.OrdinalIgnoreCase) { "vm-template", "ovf" };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<InventorySnapshot> GetInventoryAsync(InfrastructureConnectionOptions options, CancellationToken cancellationToken)
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
                ContentLibrary = await ReadContentLibraryAsync(client, sessionToken, options, cancellationToken),
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

    private HttpClient CreateClient(InfrastructureConnectionOptions options)
    {
        var client = httpClientFactory.CreateClient(InventoryHttpClients.For(HttpClientName, options));
        client.BaseAddress = new Uri(options.Url.TrimEnd('/') + "/", UriKind.Absolute);
        return client;
    }

    #region Session

    private static async Task<string> CreateSessionAsync(HttpClient client, InfrastructureConnectionOptions options, CancellationToken cancellationToken)
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
        HttpClient client, string sessionToken, InfrastructureConnectionOptions options, CancellationToken cancellationToken)
    {
        var result = await ReadTemplateVmsAsync(client, sessionToken, options, cancellationToken);

        if (result.Available && result.Entries.Length > 0)
            await AddFolderPathsAsync(client, sessionToken, result.Entries, options, cancellationToken);

        // After the walk, so a template it placed is found by its path and datacenter.
        foreach (var template in result.Entries)
        {
            template.Terraform = TerraformSnippets.VsphereTemplate(
                template.Name, template.Path, template.Properties.GetValueOrDefault("datacenter"));
        }

        return result;
    }

    private async Task<InventoryCategoryResult> ReadTemplateVmsAsync(
        HttpClient client, string sessionToken, InfrastructureConnectionOptions options, CancellationToken cancellationToken)
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

    /// <summary>
    /// Gives each template its folder path below its datacenter's VM folder, e.g.
    /// "Templates/Linux/ubuntu-22", which is the relative path Terraform's
    /// vsphere_virtual_machine data source accepts in place of a name. The
    /// Automation REST api puts no path on a VM and no parent on a folder, so the
    /// walk lists each folder's children from the datacenter's VM folder down.
    /// A template takes the path of the deepest folder that lists it, which is
    /// right whether the folders filter matches direct children or a subtree.
    /// <para>
    /// The path is extra detail, so a failed walk is logged and the templates keep
    /// their bare names. It never makes the category unavailable.
    /// </para>
    /// </summary>
    private async Task AddFolderPathsAsync(
        HttpClient client, string sessionToken, InventoryEntry[] templates, InfrastructureConnectionOptions options, CancellationToken cancellationToken)
    {
        var templateIds = templates.Select(x => x.Id).Where(x => x != null).ToHashSet(StringComparer.Ordinal);
        var placed = new Dictionary<string, (int Depth, string Folder, string Datacenter)>(StringComparer.Ordinal);
        var walked = 0;

        try
        {
            var datacenters = DeserializeList<VsphereDatacenter>(await GetAsync(client, sessionToken, DatacenterPath, cancellationToken));

            foreach (var datacenter in datacenters)
            {
                var info = DeserializeObject<VsphereDatacenterInfo>(
                    await GetAsync(client, sessionToken, $"{DatacenterPath}/{Uri.EscapeDataString(datacenter.Datacenter)}", cancellationToken));

                if (string.IsNullOrWhiteSpace(info?.VmFolder))
                    continue;

                var folders = new Queue<(string Id, string Path, int Depth)>();
                folders.Enqueue((info.VmFolder, string.Empty, 0));

                while (folders.Count > 0)
                {
                    if (++walked > MaxFolders)
                    {
                        logger.LogWarning("Stopped the vCenter folder walk at {MaxFolders} folders; templates beyond it are listed without a folder path", MaxFolders);
                        return;
                    }

                    var (folder, path, depth) = folders.Dequeue();
                    var escaped = Uri.EscapeDataString(folder);

                    foreach (var vm in DeserializeList<VsphereVm>(await GetAsync(client, sessionToken, $"{VmPath}?folders={escaped}", cancellationToken)))
                    {
                        if (vm.Vm != null && templateIds.Contains(vm.Vm) &&
                            (!placed.TryGetValue(vm.Vm, out var current) || depth > current.Depth))
                        {
                            placed[vm.Vm] = (depth, path, datacenter.Name);
                        }
                    }

                    foreach (var child in DeserializeList<VsphereFolder>(
                        await GetAsync(client, sessionToken, $"{FolderPath}?type=VIRTUAL_MACHINE&parent_folders={escaped}", cancellationToken)))
                    {
                        if (!string.IsNullOrEmpty(child.Folder))
                            folders.Enqueue((child.Folder, depth == 0 ? child.Name : $"{path}/{child.Name}", depth + 1));
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read the vCenter folders at {VsphereUrl}; some VM templates are listed without a folder path", options.Url);
        }
        finally
        {
            foreach (var template in templates)
            {
                if (template.Id == null || !placed.TryGetValue(template.Id, out var place))
                    continue;

                AddIfPresent(template.Properties, "datacenter", place.Datacenter);

                if (!string.IsNullOrEmpty(place.Folder))
                {
                    template.Path = $"{place.Folder}/{template.Name}";
                    template.Properties["folder"] = place.Folder;
                }
            }
        }
    }

    /// <summary>
    /// The VM templates kept in content libraries. Terraform finds one with the
    /// vsphere_content_library_item data source, by its name in its library, and
    /// clones it through template_uuid.
    /// </summary>
    private async Task<InventoryCategoryResult> ReadContentLibraryAsync(
        HttpClient client, string sessionToken, InfrastructureConnectionOptions options, CancellationToken cancellationToken)
    {
        const string description = "content libraries";

        try
        {
            var libraryIds = DeserializeList<string>(await GetAsync(client, sessionToken, LibraryPath, cancellationToken));
            var entries = new List<InventoryEntry>();
            var read = 0;

            foreach (var libraryId in libraryIds)
            {
                VsphereLibrary library;
                string[] itemIds;

                try
                {
                    library = DeserializeObject<VsphereLibrary>(
                        await GetAsync(client, sessionToken, $"{LibraryPath}/{Uri.EscapeDataString(libraryId)}", cancellationToken));
                    itemIds = DeserializeList<string>(
                        await GetAsync(client, sessionToken, $"{LibraryItemPath}?library_id={Uri.EscapeDataString(libraryId)}", cancellationToken));
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    // Deleted between the list and the read.
                    continue;
                }

                var libraryName = string.IsNullOrWhiteSpace(library?.Name) ? libraryId : library.Name;

                foreach (var itemId in itemIds)
                {
                    if (++read > MaxLibraryItems)
                    {
                        return InventoryCategoryResult.Unavailable(
                            $"vCenter has more than {MaxLibraryItems} content library items, more than Caster reads, so content library templates are not listed.");
                    }

                    VsphereLibraryItem item;

                    try
                    {
                        item = DeserializeObject<VsphereLibraryItem>(
                            await GetAsync(client, sessionToken, $"{LibraryItemPath}/{Uri.EscapeDataString(itemId)}", cancellationToken));
                    }
                    catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                    {
                        // Deleted between the list and the read.
                        continue;
                    }

                    if (item == null || string.IsNullOrWhiteSpace(item.Name) || !TemplateItemTypes.Contains(item.Type ?? string.Empty))
                        continue;

                    var properties = new Dictionary<string, string> { ["library"] = libraryName };
                    AddIfPresent(properties, "libraryType", library?.Type);
                    AddIfPresent(properties, "type", item.Type);
                    AddIfPresent(properties, "size", item.Size?.ToString());

                    entries.Add(new InventoryEntry
                    {
                        Id = item.Id ?? itemId,
                        Name = item.Name,
                        Path = $"{libraryName}/{item.Name}",
                        Properties = properties,
                        Terraform = TerraformSnippets.VsphereLibraryItem(item.Name, item.Type, libraryName)
                    });
                }
            }

            logger.LogInformation("Read {Count} content library templates from {LibraryCount} libraries at {VsphereUrl}", entries.Count, libraryIds.Length, options.Url);

            return InventoryCategoryResult.Ok(entries
                .OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray());
        }
        catch (HttpRequestException ex) when (ex.StatusCode.HasValue)
        {
            return InventoryCategoryResult.Unavailable(
                $"vCenter returned {(int)ex.StatusCode} {ex.StatusCode} when reading {description}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Exception reading {Description} from {VsphereUrl}", description, options.Url);
            return InventoryCategoryResult.Unavailable(Redact($"Could not read {description} from vCenter: {ex.Message}", options));
        }
    }

    private async Task<InventoryCategoryResult> ReadNetworksAsync(
        HttpClient client, string sessionToken, InfrastructureConnectionOptions options, CancellationToken cancellationToken)
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
        HttpClient client, string sessionToken, InfrastructureConnectionOptions options, CancellationToken cancellationToken)
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
        InfrastructureConnectionOptions options,
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

    /// <summary>
    /// A GET body, or an <see cref="HttpRequestException"/> carrying the status code.
    /// </summary>
    private static async Task<string> GetAsync(HttpClient client, string sessionToken, string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(SessionHeader, sessionToken);

        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"vCenter returned {(int)response.StatusCode} {response.StatusCode}", null, response.StatusCode);
        }

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    internal static T DeserializeObject<T>(string body) where T : class
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        using var document = JsonDocument.Parse(body);
        var element = document.RootElement;

        // 7.0U2+ /api returns the object, older /rest wraps it in { "value": {...} }
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("value", out var value) &&
            value.ValueKind == JsonValueKind.Object)
        {
            element = value;
        }

        return element.ValueKind == JsonValueKind.Object ? element.Deserialize<T>(JsonOptions) : null;
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

    private static string Redact(string message, InfrastructureConnectionOptions options) =>
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

    private class VsphereDatacenter
    {
        [JsonPropertyName("datacenter")]
        public string Datacenter { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }
    }

    private class VsphereDatacenterInfo
    {
        [JsonPropertyName("vm_folder")]
        public string VmFolder { get; set; }
    }

    private class VsphereFolder
    {
        [JsonPropertyName("folder")]
        public string Folder { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }
    }

    private class VsphereLibrary
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }
    }

    private class VsphereLibraryItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("size")]
        public long? Size { get; set; }
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
