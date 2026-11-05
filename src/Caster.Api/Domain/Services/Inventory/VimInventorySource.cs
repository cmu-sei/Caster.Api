// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.ServiceModel;
using System.ServiceModel.Security;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using VimClient;

namespace Caster.Api.Domain.Services.Inventory;

/// <summary>
/// What the vSphere SOAP (vim25) api says about a vCenter's inventory, as plain
/// objects. Ids are managed object reference values, e.g. "vm-1024".
/// </summary>
public class VimInventory
{
    public List<VimDatacenter> Datacenters { get; set; } = [];
    public List<VimFolder> Folders { get; set; } = [];

    /// <summary>Hosts' parents: standalone compute resources and clusters.</summary>
    public List<VimFolder> ComputeResources { get; set; } = [];
    public List<VimVm> Vms { get; set; } = [];
    public List<VimHost> Hosts { get; set; } = [];
    public List<VimNetwork> Networks { get; set; } = [];
    public List<VimDatastore> Datastores { get; set; } = [];
    public List<VimIso> Isos { get; set; } = [];

    /// <summary>Why the ISOs could not all be listed, or null when they were.</summary>
    public string IsoError { get; set; }
}

public record VimDatacenter(string Id, string Name, string VmFolder);

/// <summary>A folder, or another object that only matters as a parent, e.g. a cluster.</summary>
public record VimFolder(string Id, string Name, string Parent);

public record VimVm(string Id, string Name, bool Template, string PowerState, int? Cpus, long? MemoryMiB, string Parent, string Host);

public record VimHost(
    string Id,
    string Name,
    string ConnectionState,
    string PowerState,
    int? CpuUsageMhz,
    int? CpuMhz,
    short? Cores,
    int? MemoryUsageMiB,
    long? MemoryBytes,
    string Parent = null);

/// <param name="Type">Network, DistributedVirtualPortgroup or OpaqueNetwork.</param>
/// <param name="Uplink">A distributed switch's uplink portgroup, which VMs are not attached to.</param>
public record VimNetwork(string Id, string Name, string Type, bool Uplink, string Parent = null);

public record VimDatastore(string Id, string Name, string Type, long? Capacity, long? FreeSpace, bool Accessible, string Browser, string Parent = null);

/// <param name="Path">The datastore path, e.g. "[LocalDS_0] iso/alpine.iso".</param>
public record VimIso(string Datastore, string Path, string File, long? Size);

/// <summary>
/// Reads a vCenter's inventory over the vSphere SOAP api. Kept behind an
/// interface so the mapping into inventory categories can be tested without one.
/// </summary>
public interface IVimInventorySource
{
    /// <summary>
    /// Logs in, reads, and logs out. Throws when vCenter cannot be reached or
    /// refuses the login or the read; a failed ISO search only sets
    /// <see cref="VimInventory.IsoError"/>.
    /// </summary>
    Task<VimInventory> ReadAsync(InfrastructureConnectionOptions options, CancellationToken cancellationToken);
}

/// <summary>
/// The vim25 reads, through the same VimClient package Player and TopoMojo use.
/// Only ever logs in, reads properties, searches datastores and logs out.
/// </summary>
public class VimInventorySource(ILogger<VimInventorySource> logger) : IVimInventorySource
{
    /// <summary>Each datastore's ISO search is a vCenter task, so they stop here.</summary>
    internal const int MaxDatastoreSearches = 50;

    private static readonly TimeSpan SearchTimeout = TimeSpan.FromMinutes(2);

    public async Task<VimInventory> ReadAsync(InfrastructureConnectionOptions options, CancellationToken cancellationToken)
    {
        var client = CreateClient(options);
        ServiceContent content = null;
        var loggedIn = false;

        try
        {
            content = await client.RetrieveServiceContentAsync(new ManagedObjectReference { type = "ServiceInstance", Value = "ServiceInstance" });
            await client.LoginAsync(content.sessionManager, options.Username, options.Password, null);
            loggedIn = true;

            var objects = await RetrieveAsync(client, content.propertyCollector, InventoryFilter(content.rootFolder), cancellationToken);
            var inventory = Project(objects);

            await SearchIsosAsync(client, content.propertyCollector, inventory, objects, options.IsoLocations, cancellationToken);

            return inventory;
        }
        finally
        {
            if (loggedIn)
            {
                try
                {
                    await client.LogoutAsync(content.sessionManager);
                }
                catch (Exception ex)
                {
                    // vCenter expires idle sessions on its own.
                    logger.LogDebug(ex, "Could not log out of the vCenter inventory session");
                }
            }

            try
            {
                await client.CloseAsync();
            }
            catch
            {
                client.Abort();
            }
        }
    }

    private static VimPortTypeClient CreateClient(InfrastructureConnectionOptions options)
    {
        var client = new VimPortTypeClient(VimPortTypeClient.EndpointConfiguration.VimPort, $"{options.Url.TrimEnd('/')}/sdk");

        if (client.Endpoint.Binding is BasicHttpBinding binding)
        {
            // A large vCenter's inventory is far over the 64 KB default.
            binding.MaxReceivedMessageSize = int.MaxValue;
            binding.ReaderQuotas.MaxStringContentLength = int.MaxValue;
            binding.ReaderQuotas.MaxArrayLength = int.MaxValue;
            // The session lives in a cookie.
            binding.AllowCookies = true;
        }

        if (options.InsecureSkipVerify)
        {
            client.ClientCredentials.ServiceCertificate.SslCertificateAuthentication = new X509ServiceCertificateAuthentication
            {
                CertificateValidationMode = X509CertificateValidationMode.None,
                RevocationMode = X509RevocationMode.NoCheck
            };
        }

        return client;
    }

    /// <summary>
    /// Everything below the root folder: datacenters, their VM, host, network and
    /// datastore folders and what is in them, and each compute resource's hosts.
    /// </summary>
    private static PropertyFilterSpec InventoryFilter(ManagedObjectReference root)
    {
        SelectionSpec[] Next(params string[] names) => names.Select(x => new SelectionSpec { name = x }).ToArray();
        var all = new[] { "folder", "dcVm", "dcHost", "dcNetwork", "dcDatastore", "computeHost" };

        return new PropertyFilterSpec
        {
            objectSet =
            [
                new ObjectSpec
                {
                    obj = root,
                    skip = false,
                    selectSet =
                    [
                        new TraversalSpec { name = "folder", type = "Folder", path = "childEntity", skip = false, selectSet = Next(all) },
                        new TraversalSpec { name = "dcVm", type = "Datacenter", path = "vmFolder", skip = false, selectSet = Next("folder") },
                        new TraversalSpec { name = "dcHost", type = "Datacenter", path = "hostFolder", skip = false, selectSet = Next("folder") },
                        new TraversalSpec { name = "dcNetwork", type = "Datacenter", path = "networkFolder", skip = false, selectSet = Next("folder") },
                        new TraversalSpec { name = "dcDatastore", type = "Datacenter", path = "datastoreFolder", skip = false, selectSet = Next("folder") },
                        new TraversalSpec { name = "computeHost", type = "ComputeResource", path = "host", skip = false }
                    ]
                }
            ],
            propSet =
            [
                new PropertySpec { type = "Datacenter", pathSet = ["name", "vmFolder"] },
                new PropertySpec { type = "Folder", pathSet = ["name", "parent"] },
                new PropertySpec
                {
                    type = "VirtualMachine",
                    pathSet = ["name", "config.template", "runtime.powerState", "config.hardware.numCPU", "config.hardware.memoryMB", "parent", "runtime.host"]
                },
                new PropertySpec
                {
                    type = "HostSystem",
                    pathSet =
                    [
                        "name", "runtime.connectionState", "runtime.powerState",
                        "summary.quickStats.overallCpuUsage", "summary.quickStats.overallMemoryUsage",
                        "summary.hardware.cpuMhz", "summary.hardware.numCpuCores", "summary.hardware.memorySize", "parent"
                    ]
                },
                // Subtypes too, e.g. ClusterComputeResource. A host's parent, for its datacenter.
                new PropertySpec { type = "ComputeResource", pathSet = ["name", "parent"] },
                new PropertySpec { type = "Network", pathSet = ["name", "parent"] },
                new PropertySpec { type = "DistributedVirtualPortgroup", pathSet = ["name", "config.uplink", "tag", "parent"] },
                // Subtypes too, e.g. VmwareDistributedVirtualSwitch.
                new PropertySpec { type = "DistributedVirtualSwitch", pathSet = ["config.uplinkPortgroup"] },
                new PropertySpec
                {
                    type = "Datastore",
                    pathSet = ["summary.name", "summary.type", "summary.capacity", "summary.freeSpace", "summary.accessible", "browser", "parent"]
                }
            ]
        };
    }

    /// <summary>RetrievePropertiesEx, following its continuation tokens.</summary>
    private static async Task<List<ObjectContent>> RetrieveAsync(
        VimPortTypeClient client, ManagedObjectReference collector, PropertyFilterSpec filter, CancellationToken cancellationToken)
    {
        var objects = new List<ObjectContent>();
        var result = (await client.RetrievePropertiesExAsync(collector, [filter], new RetrieveOptions())).returnval;

        while (result != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            objects.AddRange(result.objects ?? []);

            if (string.IsNullOrEmpty(result.token))
                break;

            result = await client.ContinueRetrievePropertiesExAsync(collector, result.token);
        }

        return objects;
    }

    public static VimInventory Project(IEnumerable<ObjectContent> objects)
    {
        var inventory = new VimInventory();
        var uplinks = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in objects)
        {
            var id = item.obj?.Value;
            var values = (item.propSet ?? []).ToDictionary(x => x.name, x => x.val, StringComparer.Ordinal);

            switch (item.obj?.type)
            {
                case "Datacenter":
                    inventory.Datacenters.Add(new VimDatacenter(id, Text(values, "name"), Reference(values, "vmFolder")));
                    break;
                case "Folder":
                // A datastore cluster, which is a folder of datastores.
                case "StoragePod":
                    inventory.Folders.Add(new VimFolder(id, Text(values, "name"), Reference(values, "parent")));
                    break;
                case "VirtualMachine":
                    inventory.Vms.Add(new VimVm(
                        id,
                        Text(values, "name"),
                        Value<bool>(values, "config.template") ?? false,
                        Text(values, "runtime.powerState"),
                        Value<int>(values, "config.hardware.numCPU"),
                        Value<int>(values, "config.hardware.memoryMB"),
                        Reference(values, "parent"),
                        Reference(values, "runtime.host")));
                    break;
                case "HostSystem":
                    inventory.Hosts.Add(new VimHost(
                        id,
                        Text(values, "name"),
                        Text(values, "runtime.connectionState"),
                        Text(values, "runtime.powerState"),
                        Value<int>(values, "summary.quickStats.overallCpuUsage"),
                        Value<int>(values, "summary.hardware.cpuMhz"),
                        Value<short>(values, "summary.hardware.numCpuCores"),
                        Value<int>(values, "summary.quickStats.overallMemoryUsage"),
                        Value<long>(values, "summary.hardware.memorySize"),
                        Reference(values, "parent")));
                    break;
                case "ComputeResource":
                case "ClusterComputeResource":
                    inventory.ComputeResources.Add(new VimFolder(id, Text(values, "name"), Reference(values, "parent")));
                    break;
                case "Network":
                case "OpaqueNetwork":
                case "DistributedVirtualPortgroup":
                    // Both the Network and the portgroup spec can match a portgroup.
                    var existing = inventory.Networks.FindIndex(x => x.Id == id);
                    var network = new VimNetwork(id, Text(values, "name"), item.obj.type, IsUplink(values), Reference(values, "parent"));

                    if (existing < 0)
                        inventory.Networks.Add(network);
                    else if (network.Uplink)
                        inventory.Networks[existing] = network with { Parent = network.Parent ?? inventory.Networks[existing].Parent };
                    else if (inventory.Networks[existing].Parent == null)
                        inventory.Networks[existing] = inventory.Networks[existing] with { Parent = network.Parent };
                    break;
                case "DistributedVirtualSwitch":
                case "VmwareDistributedVirtualSwitch":
                    if (values.TryGetValue("config.uplinkPortgroup", out var portgroups) && portgroups is ManagedObjectReference[] refs)
                        uplinks.UnionWith(refs.Where(x => x?.Value != null).Select(x => x.Value));
                    break;
                case "Datastore":
                    inventory.Datastores.Add(new VimDatastore(
                        id,
                        Text(values, "summary.name"),
                        Text(values, "summary.type"),
                        Value<long>(values, "summary.capacity"),
                        Value<long>(values, "summary.freeSpace"),
                        Value<bool>(values, "summary.accessible") ?? false,
                        Reference(values, "browser"),
                        Reference(values, "parent")));
                    break;
            }
        }

        // A switch names its uplink portgroups, whichever order they were read in.
        for (var i = 0; i < inventory.Networks.Count; i++)
        {
            if (uplinks.Contains(inventory.Networks[i].Id))
                inventory.Networks[i] = inventory.Networks[i] with { Uplink = true };
        }

        return inventory;
    }

    /// <summary>
    /// Where to search for ISOs: only the IsoLocations entries, each a datastore
    /// name ("iso-ds") or a datastore path ("[iso-ds] stock"). With none, nothing
    /// is searched and IsoError says what to set: a recursive search of every
    /// datastore is slow on a large vCenter and lists the ISOs inside VM folders
    /// too. Every search is a vCenter task, so a location that fails or times out
    /// is reported in IsoError and the rest are still listed.
    /// </summary>
    private async Task SearchIsosAsync(
        VimPortTypeClient client, ManagedObjectReference collector, VimInventory inventory, List<ObjectContent> objects,
        string[] isoLocations, CancellationToken cancellationToken)
    {
        var browsers = objects
            .Where(x => x.obj?.type == "Datastore")
            .Select(x => (Datastore: inventory.Datastores.FirstOrDefault(d => d.Id == x.obj.Value), Browser: x.propSet?.FirstOrDefault(p => p.name == "browser")?.val as ManagedObjectReference))
            .Where(x => x.Datastore != null && x.Datastore.Accessible && x.Browser != null && !string.IsNullOrEmpty(x.Datastore.Name))
            .ToDictionary(x => x.Datastore.Name, x => x.Browser, StringComparer.Ordinal);

        if (!(isoLocations ?? []).Any(x => !string.IsNullOrWhiteSpace(x)))
        {
            var datastores = browsers.Count == 0
                ? "There are no accessible datastores."
                : $"Accessible datastores: {string.Join(", ", browsers.Keys.Order(StringComparer.Ordinal))}.";
            inventory.IsoError =
                "ISOs are not listed until this connection's IsoLocations names the datastores or folders that hold them, " +
                $"e.g. \"iso-datastore\" or \"[iso-datastore] stock\". {datastores}";
            return;
        }

        var (targets, unknown) = IsoTargets(isoLocations, browsers.Keys);
        var failed = new List<string>();

        if (unknown.Length > 0)
        {
            var available = browsers.Count == 0 ? "There are no accessible datastores" : $"Accessible datastores: {string.Join(", ", browsers.Keys.Order(StringComparer.Ordinal))}";
            failed.Add($"Infrastructure:IsoLocations names {string.Join(", ", unknown.Select(x => $"\"{x}\""))}, which is not an accessible datastore. {available}");
        }

        if (targets.Length > MaxDatastoreSearches)
            failed.Add($"only the first {MaxDatastoreSearches} of {targets.Length} datastores were searched");

        foreach (var (name, folder) in targets.Take(MaxDatastoreSearches))
        {
            var root = folder.Length == 0 ? $"[{name}]" : $"[{name}] {folder}";

            try
            {
                var task = await client.SearchDatastoreSubFolders_TaskAsync(
                    browsers[name],
                    root,
                    new HostDatastoreBrowserSearchSpec
                    {
                        matchPattern = ["*.iso"],
                        details = new FileQueryFlags { fileSize = true, fileType = true, modification = false, fileOwner = false, fileOwnerSpecified = true }
                    });

                var info = await WaitAsync(client, collector, task, cancellationToken);

                if (info.state != TaskInfoState.success)
                {
                    failed.Add($"{root}: {info.error?.localizedMessage ?? info.state.ToString()}");
                    continue;
                }

                foreach (var result in (info.result as HostDatastoreBrowserSearchResults[]) ?? [])
                {
                    foreach (var file in result.file ?? [])
                    {
                        inventory.Isos.Add(new VimIso(name, DatastorePath(name, result.folderPath, file.path), file.path, file.fileSizeSpecified ? file.fileSize : null));
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not search {DatastorePath} for ISOs", root);
                failed.Add($"{root}: {ex.Message}");
            }
        }

        if (failed.Count > 0)
            inventory.IsoError = $"Some ISO locations could not be searched: {string.Join("; ", failed)}.";
    }

    /// <summary>
    /// The datastores and folders to search; no locations means none. A
    /// datastore named twice, or once whole and once by folder, is searched once
    /// per distinct folder, and its root wins over its folders.
    /// </summary>
    public static ((string Datastore, string Folder)[] Targets, string[] Unknown) IsoTargets(
        string[] isoLocations, IEnumerable<string> datastores)
    {
        var known = datastores.ToHashSet(StringComparer.Ordinal);
        var locations = (isoLocations ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();

        if (locations.Length == 0)
            return ([], []);

        var targets = new List<(string Datastore, string Folder)>();
        var unknown = new List<string>();

        foreach (var entry in locations.Select(x => x.Trim()))
        {
            string datastore, folder;

            if (entry.StartsWith('[') && entry.IndexOf(']') is var close and > 0)
            {
                datastore = entry[1..close];
                folder = entry[(close + 1)..].Trim().Trim('/');
            }
            else
            {
                datastore = entry;
                folder = string.Empty;
            }

            if (!known.Contains(datastore))
            {
                unknown.Add(entry);
                continue;
            }

            targets.Add((datastore, folder));
        }

        var roots = targets.Where(x => x.Folder.Length == 0).Select(x => x.Datastore).ToHashSet(StringComparer.Ordinal);

        return (targets
            .Where(x => x.Folder.Length == 0 || !roots.Contains(x.Datastore))
            .Distinct()
            .ToArray(), unknown.ToArray());
    }

    private static async Task<TaskInfo> WaitAsync(
        VimPortTypeClient client, ManagedObjectReference collector, ManagedObjectReference task, CancellationToken cancellationToken)
    {
        var filter = new PropertyFilterSpec
        {
            objectSet = [new ObjectSpec { obj = task, skip = false }],
            propSet = [new PropertySpec { type = "Task", pathSet = ["info"] }]
        };
        var deadline = DateTime.UtcNow + SearchTimeout;

        while (true)
        {
            var objects = await RetrieveAsync(client, collector, filter, cancellationToken);
            var info = objects.SelectMany(x => x.propSet ?? []).Select(x => x.val).OfType<TaskInfo>().FirstOrDefault();

            if (info != null && info.state is TaskInfoState.success or TaskInfoState.error)
                return info;

            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"the search did not finish within {SearchTimeout.TotalSeconds:0} seconds");

            await Task.Delay(500, cancellationToken);
        }
    }

    /// <summary>
    /// The standard "[datastore] folder/file" form. vCenter returns folders as
    /// "[ds] iso/", and some servers as "[ds]/iso".
    /// </summary>
    public static string DatastorePath(string datastore, string folderPath, string file)
    {
        var folder = folderPath ?? string.Empty;
        var close = folder.IndexOf(']');
        var relative = (close >= 0 ? folder[(close + 1)..] : folder).Trim().Trim('/');

        return relative.Length == 0 ? $"[{datastore}] {file}" : $"[{datastore}] {relative}/{file}";
    }

    /// <summary>
    /// The portgroup's own word for it. Not every vCenter sets config.uplink or
    /// the tag, so the switch's uplinkPortgroup list counts too (see Project).
    /// </summary>
    private static bool IsUplink(Dictionary<string, object> values) =>
        (Value<bool>(values, "config.uplink") ?? false) ||
        (values.TryGetValue("tag", out var tags) && tags is Tag[] list &&
            list.Any(x => string.Equals(x?.key, UplinkTag, StringComparison.Ordinal)));

    private const string UplinkTag = "SYSTEM/DVS.UPLINKPG";

    private static string Text(Dictionary<string, object> values, string name) =>
        values.TryGetValue(name, out var value) && value != null ? value.ToString() : null;

    private static string Reference(Dictionary<string, object> values, string name) =>
        values.TryGetValue(name, out var value) ? (value as ManagedObjectReference)?.Value : null;

    private static T? Value<T>(Dictionary<string, object> values, string name) where T : struct =>
        values.TryGetValue(name, out var value) && value != null
            ? value is T typed ? typed : (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture)
            : null;
}
