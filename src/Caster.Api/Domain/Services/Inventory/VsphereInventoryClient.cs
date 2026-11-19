// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
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
/// Read-only inventory reader for vCenter. Templates, networks, datastores, hosts
/// and ISOs come from the vSphere SOAP (vim25) api, as in Player and TopoMojo,
/// since only it has host usage, folder parents and datastore browsing. Content
/// libraries are not in vim25, so they come from the Automation REST api, which
/// is only ever sent POST /api/session, GET and DELETE /api/session.
/// </summary>
public class VsphereInventoryClient(
    IHttpClientFactory httpClientFactory,
    IVimInventorySource vimSource,
    ILogger<VsphereInventoryClient> logger) : IInventoryProvider
{
    public const string HttpClientName = "vsphere-inventory";

    string IInventoryProvider.Provider => InfrastructureProviders.Vsphere;

    string IInventoryProvider.HttpClientName => HttpClientName;

    private const string SessionHeader = "vmware-api-session-id";
    private const string SessionPath = "api/session";
    private const string LibraryPath = "api/content/library";
    private const string LibraryItemPath = "api/content/library/item";

    /// <summary>A folder chain deeper than this is treated as broken.</summary>
    private const int MaxFolderDepth = 64;

    /// <summary>
    /// Each content library item costs a request. Past this the category reports
    /// itself unavailable rather than list some of the templates, since a partial
    /// list would make a template that exists look missing.
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

        VimInventory vim = null;
        string vimError = null;

        try
        {
            vim = await vimSource.ReadAsync(options, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            vimError = Redact($"Could not read vCenter: {ex.Message}", options);
            logger.LogError("Could not read the vCenter inventory at {VsphereUrl} as {VsphereUsername}: {Error}",
                options.Url, options.Username, InventoryRedaction.Describe(ex, options));
        }

        // Content libraries cost a request per item, so they're only read when wanted.
        var (contentLibrary, connected) = InventoryFilters.IsEnabled(options, InventoryCategory.ContentLibrary)
            ? await ReadContentLibraryAsync(options, cancellationToken)
            : (InventoryCategoryResult.NotEnabled(null), false);

        // Nothing reached vCenter, so there is no read to keep.
        if (vim == null && !connected)
            return InventorySnapshot.AllUnavailable(vimError);

        var unavailable = vim == null ? InventoryCategoryResult.Unavailable(vimError) : null;

        return new InventorySnapshot
        {
            LastUpdated = DateTime.UtcNow,
            VmTemplates = unavailable ?? Templates(vim),
            Networks = unavailable ?? Networks(vim),
            Datastores = unavailable ?? Datastores(vim),
            Hosts = unavailable ?? Hosts(vim),
            Isos = unavailable ?? Isos(vim),
            ContentLibrary = contentLibrary
        };
    }

    #region vim25 categories

    /// <summary>
    /// Each template's path is its full inventory path, as govc writes it, e.g.
    /// "/DC1/vm/Templates/Linux/ubuntu-22". Its snippet uses the path below the
    /// datacenter's VM folder, "Templates/Linux/ubuntu-22", which Terraform's
    /// vsphere_virtual_machine data source accepts in place of a name. A template
    /// whose folders cannot be followed up to a datacenter keeps its bare name.
    /// </summary>
    internal static InventoryCategoryResult Templates(VimInventory vim)
    {
        var folders = vim.Folders.Where(x => x.Id != null).DistinctBy(x => x.Id).ToDictionary(x => x.Id, StringComparer.Ordinal);
        var datacenters = vim.Datacenters.Where(x => x.VmFolder != null).DistinctBy(x => x.VmFolder).ToDictionary(x => x.VmFolder, StringComparer.Ordinal);
        var paths = new InventoryPaths(vim);

        return InventoryCategoryResult.Ok(vim.Vms
            .Where(x => x.Template && !string.IsNullOrWhiteSpace(x.Name))
            .Select(x =>
            {
                var properties = new Dictionary<string, string>();
                AddIfPresent(properties, "powerState", x.PowerState);
                AddIfPresent(properties, "cpuCount", x.Cpus?.ToString(CultureInfo.InvariantCulture));
                AddIfPresent(properties, "memorySizeMiB", x.MemoryMiB?.ToString(CultureInfo.InvariantCulture));

                var (datacenter, folder) = Place(x.Parent, folders, datacenters);
                AddIfPresent(properties, "datacenter", datacenter);
                AddIfPresent(properties, "folder", folder);

                var path = paths.Of(x.Name, x.Parent);

                return new InventoryEntry
                {
                    Id = x.Id,
                    Name = x.Name,
                    Path = path ?? x.Name,
                    Properties = properties,
                    Locations = path == null ? [] : [path],
                    Terraform = TerraformSnippets.VsphereTemplate(x.Name, path, datacenter, paths.OfDatacenterAbove(x.Parent))
                };
            })
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray());
    }

    /// <summary>
    /// Follows a VM's parent folders up to a datacenter's VM folder. Returns the
    /// datacenter's name and the folders between, or nulls when the chain does not
    /// reach one.
    /// </summary>
    private static (string Datacenter, string Folder) Place(
        string parent, Dictionary<string, VimFolder> folders, Dictionary<string, VimDatacenter> datacenters)
    {
        var names = new List<string>();
        var current = parent;

        for (var depth = 0; current != null && depth < MaxFolderDepth; depth++)
        {
            if (datacenters.TryGetValue(current, out var datacenter))
            {
                names.Reverse();
                return (datacenter.Name, string.Join("/", names));
            }

            if (!folders.TryGetValue(current, out var folder))
                break;

            names.Add(folder.Name);
            current = folder.Parent;
        }

        return (null, null);
    }

    /// <summary>
    /// Full inventory paths, as govc writes them, e.g. "/DC1/network/Lab/lab-net": every
    /// name from below the root folder down to the object, which vSphere itself shows
    /// under the root folder "Datacenters".
    /// </summary>
    internal sealed class InventoryPaths
    {
        private readonly Dictionary<string, (string Name, string Parent)> _parents;
        private readonly HashSet<string> _datacenters;

        public InventoryPaths(VimInventory vim)
        {
            _parents = vim.Folders.Concat(vim.ComputeResources)
                .Where(x => x.Id != null)
                .Select(x => (x.Id, x.Name, x.Parent))
                .Concat(vim.Datacenters.Where(x => x.Id != null).Select(x => (x.Id, x.Name, x.Parent)))
                .DistinctBy(x => x.Id)
                .ToDictionary(x => x.Id, x => (x.Name, x.Parent), StringComparer.Ordinal);
            _datacenters = vim.Datacenters.Where(x => x.Id != null).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        }

        /// <summary>
        /// The path of an object called <paramref name="name"/> whose parent is
        /// <paramref name="parent"/>, or null when its parents don't reach the root.
        /// </summary>
        public string Of(string name, string parent)
        {
            var above = OfFolder(parent);
            return above == null ? null : $"{above}/{name}";
        }

        /// <summary>The path of the datacenter an object is in, or null.</summary>
        public string OfDatacenterAbove(string parent)
        {
            var current = parent;

            for (var depth = 0; current != null && depth < MaxFolderDepth; depth++)
            {
                if (_datacenters.Contains(current))
                    return OfFolder(current);

                if (!_parents.TryGetValue(current, out var next))
                    break;

                current = next.Parent;
            }

            return null;
        }

        /// <summary>The path of a folder, datacenter or cluster by id; "" for the root folder.</summary>
        private string OfFolder(string id)
        {
            var names = new List<string>();
            var current = id;

            for (var depth = 0; depth < MaxFolderDepth; depth++)
            {
                if (current == null || !_parents.TryGetValue(current, out var node))
                    return null;

                // The root folder has no parent and isn't part of the path. A datacenter
                // read without its parent is taken to be directly below it.
                if (node.Parent == null && !_datacenters.Contains(current))
                    return Joined(names);

                names.Add(node.Name);

                if (node.Parent == null)
                    return Joined(names);

                current = node.Parent;
            }

            return null;
        }

        private static string Joined(List<string> names)
        {
            names.Reverse();
            return names.Count == 0 ? "" : "/" + string.Join("/", names);
        }
    }

    /// <summary>
    /// The name of the datacenter an object is in, found by following its parents
    /// (folders, datastore clusters, clusters) up to a datacenter, or null when
    /// they do not reach one.
    /// </summary>
    private static Func<string, string> DatacenterOf(VimInventory vim)
    {
        var parents = vim.Folders.Concat(vim.ComputeResources)
            .Where(x => x.Id != null)
            .DistinctBy(x => x.Id)
            .ToDictionary(x => x.Id, x => x.Parent, StringComparer.Ordinal);
        var datacenters = vim.Datacenters.Where(x => x.Id != null).DistinctBy(x => x.Id).ToDictionary(x => x.Id, x => x.Name, StringComparer.Ordinal);

        return parent =>
        {
            var current = parent;

            for (var depth = 0; current != null && depth < MaxFolderDepth; depth++)
            {
                if (datacenters.TryGetValue(current, out var datacenter))
                    return datacenter;

                if (!parents.TryGetValue(current, out current))
                    break;
            }

            return null;
        };
    }

    /// <summary>
    /// Portgroups and standard networks. A distributed switch's uplink portgroup
    /// is left out, since VMs cannot attach to it.
    /// </summary>
    internal static InventoryCategoryResult Networks(VimInventory vim)
    {
        var datacenterOf = DatacenterOf(vim);
        var paths = new InventoryPaths(vim);
        var switches = vim.Switches.Where(x => x.Id != null).DistinctBy(x => x.Id).ToDictionary(x => x.Id, x => x.Name, StringComparer.Ordinal);

        return InventoryCategoryResult.Ok(vim.Networks
            .Where(x => !x.Uplink && !string.IsNullOrWhiteSpace(x.Name))
            .Select(x =>
            {
                var properties = new Dictionary<string, string>();
                AddIfPresent(properties, "type", x.Type switch
                {
                    "DistributedVirtualPortgroup" => "DISTRIBUTED_PORTGROUP",
                    "OpaqueNetwork" => "OPAQUE_NETWORK",
                    _ => "STANDARD_PORTGROUP"
                });

                var datacenter = datacenterOf(x.Parent);
                AddIfPresent(properties, "datacenter", datacenter);

                // Matched by managed object reference, so switches that share a name stay apart.
                var switchName = x.Switch == null ? null : switches.GetValueOrDefault(x.Switch);
                AddIfPresent(properties, "switch", switchName);

                var path = paths.Of(x.Name, x.Parent);

                return new InventoryEntry
                {
                    Id = x.Id,
                    Name = x.Name,
                    Path = path ?? x.Name,
                    Properties = properties,
                    Locations = new[] { path, switchName }.Where(l => l != null).ToArray(),
                    Terraform = TerraformSnippets.VsphereNetwork(x.Name, datacenter)
                };
            })
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray());
    }

    internal static InventoryCategoryResult Datastores(VimInventory vim)
    {
        var datacenterOf = DatacenterOf(vim);
        var paths = new InventoryPaths(vim);

        return InventoryCategoryResult.Ok(vim.Datastores
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .Select(x =>
            {
                var properties = new Dictionary<string, string>();
                AddIfPresent(properties, "type", x.Type);
                AddIfPresent(properties, "capacity", x.Capacity?.ToString(CultureInfo.InvariantCulture));
                AddIfPresent(properties, "freeSpace", x.FreeSpace?.ToString(CultureInfo.InvariantCulture));
                properties["accessible"] = x.Accessible ? "true" : "false";

                var datacenter = datacenterOf(x.Parent);
                AddIfPresent(properties, "datacenter", datacenter);

                var path = paths.Of(x.Name, x.Parent);

                return new InventoryEntry
                {
                    Id = x.Id,
                    Name = x.Name,
                    Path = path ?? x.Name,
                    Properties = properties,
                    Locations = path == null ? [] : [path],
                    Terraform = TerraformSnippets.VsphereDatastore(x.Name, datacenter)
                };
            })
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray());
    }

    /// <summary>
    /// The ESXi hosts, with their usage and capacity and what their VMs are
    /// allocated. cpu, maxcpu, mem and maxmem are in the same units as a Proxmox
    /// node's (a fraction of the host, cores, bytes, bytes) so hosts from either
    /// provider can be shown alike; the vSphere units are alongside.
    /// </summary>
    internal static InventoryCategoryResult Hosts(VimInventory vim)
    {
        var vms = vim.Vms.Where(x => !x.Template && x.Host != null).ToLookup(x => x.Host, StringComparer.Ordinal);
        var datacenterOf = DatacenterOf(vim);
        var paths = new InventoryPaths(vim);

        return InventoryCategoryResult.Ok(vim.Hosts
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .Select(x =>
            {
                var properties = new Dictionary<string, string>();
                AddIfPresent(properties, "connectionState", x.ConnectionState);
                AddIfPresent(properties, "powerState", x.PowerState);
                AddIfPresent(properties, "cpuUsageMhz", x.CpuUsageMhz?.ToString(CultureInfo.InvariantCulture));
                AddIfPresent(properties, "maxcpu", x.Cores?.ToString(CultureInfo.InvariantCulture));
                AddIfPresent(properties, "maxmem", x.MemoryBytes?.ToString(CultureInfo.InvariantCulture));

                var capacityMhz = (long?)x.CpuMhz * x.Cores;
                AddIfPresent(properties, "cpuCapacityMhz", capacityMhz?.ToString(CultureInfo.InvariantCulture));

                if (x.CpuUsageMhz.HasValue && capacityMhz > 0)
                    properties["cpu"] = ((double)x.CpuUsageMhz.Value / capacityMhz.Value).ToString("0.####", CultureInfo.InvariantCulture);

                if (x.MemoryUsageMiB.HasValue)
                {
                    properties["memoryUsageMiB"] = x.MemoryUsageMiB.Value.ToString(CultureInfo.InvariantCulture);
                    properties["mem"] = (x.MemoryUsageMiB.Value * 1024L * 1024L).ToString(CultureInfo.InvariantCulture);
                }

                var guests = vms[x.Id].ToArray();
                var running = guests.Where(v => string.Equals(v.PowerState, "poweredOn", StringComparison.OrdinalIgnoreCase)).ToArray();
                properties["vmsTotal"] = guests.Length.ToString(CultureInfo.InvariantCulture);
                properties["vmsRunning"] = running.Length.ToString(CultureInfo.InvariantCulture);
                properties["allocatedCpus"] = running.Sum(v => v.Cpus ?? 0).ToString(CultureInfo.InvariantCulture);
                properties["allocatedMemoryMiB"] = running.Sum(v => v.MemoryMiB ?? 0).ToString(CultureInfo.InvariantCulture);

                var datacenter = datacenterOf(x.Parent);
                AddIfPresent(properties, "datacenter", datacenter);

                var name = string.IsNullOrWhiteSpace(x.Name) ? x.Id : x.Name;
                var path = paths.Of(name, x.Parent);

                return new InventoryEntry
                {
                    Id = x.Id,
                    Name = name,
                    Path = path ?? name,
                    Properties = properties,
                    Locations = path == null ? [] : [path],
                    // A host without a name cannot be looked up by one.
                    Terraform = string.IsNullOrWhiteSpace(x.Name) ? [] : TerraformSnippets.VsphereHost(x.Name, datacenter)
                };
            })
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray());
    }

    /// <summary>
    /// An ISO's name is its path on its datastore, e.g. "iso/alpine.iso", which
    /// is what a vsphere_virtual_machine cdrom block's path takes alongside the
    /// datastore; its path is the full datastore path, "[ds] iso/alpine.iso". A
    /// partial list is not returned, since it would make an ISO that exists look
    /// missing.
    /// </summary>
    internal static InventoryCategoryResult Isos(VimInventory vim)
    {
        if (vim.IsoError != null)
            return InventoryCategoryResult.Unavailable(vim.IsoError);

        // ISOs name their datastore. A name used in more than one datacenter is
        // left without one, rather than guess.
        var datacenterOf = DatacenterOf(vim);
        var datastoreDatacenters = vim.Datastores
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => x.Name, StringComparer.Ordinal)
            .ToDictionary(
                x => x.Key,
                x => x.Select(d => datacenterOf(d.Parent)).Distinct(StringComparer.Ordinal).ToArray() is [var only] ? only : null,
                StringComparer.Ordinal);

        return InventoryCategoryResult.Ok(vim.Isos
            .Select(x =>
            {
                var close = x.Path.IndexOf(']');
                var relative = close >= 0 ? x.Path[(close + 1)..].Trim() : x.Path;
                var properties = new Dictionary<string, string> { ["datastore"] = x.Datastore };
                AddIfPresent(properties, "size", x.Size?.ToString(CultureInfo.InvariantCulture));

                var datacenter = x.Datastore == null ? null : datastoreDatacenters.GetValueOrDefault(x.Datastore);
                AddIfPresent(properties, "datacenter", datacenter);

                return new InventoryEntry
                {
                    Id = x.Path,
                    Name = relative,
                    Path = x.Path,
                    Properties = properties,
                    Terraform = TerraformSnippets.VsphereIso(relative, x.Datastore, datacenter)
                };
            })
            .DistinctBy(x => x.Path, StringComparer.Ordinal)
            .OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray());
    }

    #endregion

    #region Content libraries (REST)

    private HttpClient CreateClient(InfrastructureConnectionOptions options)
    {
        var client = httpClientFactory.CreateClient(InventoryHttpClients.For(HttpClientName, options));
        client.BaseAddress = new Uri(options.Url.TrimEnd('/') + "/", UriKind.Absolute);
        return client;
    }

    /// <summary>
    /// The content library read over REST, and whether a REST session was made.
    /// </summary>
    private async Task<(InventoryCategoryResult Result, bool Connected)> ReadContentLibraryAsync(
        InfrastructureConnectionOptions options, CancellationToken cancellationToken)
    {
        using var client = CreateClient(options);
        string sessionToken;

        try
        {
            sessionToken = await CreateSessionAsync(client, options, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError("Could not create a vCenter REST session for {VsphereUrl} as {VsphereUsername}: {Error}",
                options.Url, options.Username, InventoryRedaction.Describe(ex, options));
            return (InventoryCategoryResult.Unavailable(
                Redact($"Could not connect to the vCenter REST api to read content libraries: {ex.Message}", options)), false);
        }

        try
        {
            return (await ReadContentLibraryAsync(client, sessionToken, options, cancellationToken), true);
        }
        finally
        {
            await DeleteSessionAsync(client, sessionToken, cancellationToken);
        }
    }


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
            logger.LogDebug("Could not delete the vCenter inventory session: {ExceptionType}", ex.GetType().Name);
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
            logger.LogError("Exception reading {Description} from {VsphereUrl}: {Error}",
                description, options.Url, InventoryRedaction.Describe(ex, options));
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

    #endregion
}
