// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Caster.Api.Domain.Services.Inventory;
using Caster.Api.Infrastructure.Options;

namespace Caster.Api.Domain.Services.Capacity;

/// <summary>Where the room on the hypervisors comes from. Replaced in tests.</summary>
public interface IHypervisorRoomSource
{
    HypervisorRoom Read(CapacityOptions options);
}

/// <summary>
/// The room on every host and datastore of every connection with inventory
/// enabled, from the cached inventory. Never reads a provider itself.
/// </summary>
public class InventoryRoomSource(IInventoryService inventory) : IHypervisorRoomSource
{
    public HypervisorRoom Read(CapacityOptions options)
    {
        var connections = inventory.GetConnections().Where(x => x.Enabled && x.Error == null).ToList();
        var snapshots = connections
            .Select(x => (x.Name, x.Provider, Snapshot: inventory.GetSnapshot(x.Name)))
            .Where(x => x.Snapshot != null)
            .ToList();

        return HypervisorRoomReader.Read(snapshots, options);
    }
}

/// <summary>
/// Works out each host's and datastore's free room from an inventory snapshot.
/// </summary>
/// <remarks>
/// <para>
/// Free vCPU is the host's cores times <see cref="CapacityOptions.CpuOvercommitRatio"/>,
/// less the vCPUs its running VMs are given (<c>allocatedCpus</c>). A host that
/// does not say what its VMs are given has its current load (<c>cpu</c>, a fraction
/// of the host) times its cores, rounded up, taken off instead.
/// </para>
/// <para>
/// Free memory is the host's memory less <see cref="CapacityOptions.ReservePercent"/>,
/// less the memory in use now (<c>mem</c>). Free disk is a datastore's free space
/// less the reserve.
/// </para>
/// </remarks>
public static class HypervisorRoomReader
{
    private const double Mib = 1024d * 1024d;
    private const double Gib = 1024d * 1024d * 1024d;

    public static HypervisorRoom Read(
        IEnumerable<(string Connection, string Provider, InventorySnapshot Snapshot)> snapshots,
        CapacityOptions options)
    {
        var room = new HypervisorRoom();
        var ratio = options.CpuOvercommitRatio > 0 ? options.CpuOvercommitRatio : 1;
        var keep = 1 - Math.Clamp(options.ReservePercent, 0, 100) / 100;

        foreach (var (connection, provider, snapshot) in snapshots)
        {
            var hypervisor = provider?.ToLowerInvariant();

            if (snapshot.Hosts is { Available: true } hosts)
            {
                room.HostsRead.Add(hypervisor);
                Track(room, connection, "hosts", hosts);

                foreach (var entry in hosts.Entries)
                    room.Hosts.Add(Host(connection, hypervisor, entry, ratio, keep));
            }

            if (snapshot.Datastores is { Available: true } datastores)
            {
                var sized = datastores.Entries.Where(x => Long(x, "freeSpace") != null && Usable(hypervisor, x)).ToList();

                if (sized.Count > 0)
                {
                    room.DatastoresRead.Add(hypervisor);
                    Track(room, connection, "datastores", datastores);
                }

                foreach (var entry in sized)
                {
                    room.Datastores.Add(new DatastoreRoom
                    {
                        Connection = connection,
                        Hypervisor = hypervisor,
                        Id = entry.Id,
                        Name = entry.Name,
                        FreeGiB = Math.Round(Long(entry, "freeSpace").Value * keep / Gib, 1),
                    });
                }
            }
        }

        return room;
    }

    public static HostRoom Host(string connection, string hypervisor, InventoryEntry entry, double ratio, double keep)
    {
        var cores = (int)(Long(entry, "maxcpu") ?? 0);
        var memory = (long)((Long(entry, "maxmem") ?? 0) / Mib);
        var used = (long)Math.Ceiling((Long(entry, "mem") ?? 0) / Mib);

        var host = new HostRoom
        {
            Connection = connection,
            Hypervisor = hypervisor,
            Id = entry.Id,
            Name = entry.Name,
            Online = Online(hypervisor, entry),
            Cores = cores,
            MemoryMiB = memory,
            UsedMemoryMiB = used,
        };

        if (Long(entry, "allocatedCpus") is { } allocated)
        {
            host.AllocatedVcpu = (int)allocated;
        }
        else
        {
            host.CpuApproximated = true;
            host.AllocatedVcpu = (int)Math.Ceiling((Double(entry, "cpu") ?? 0) * cores);
        }

        if (host.Online)
        {
            host.FreeVcpu = Math.Max(0, (int)Math.Floor(cores * ratio) - host.AllocatedVcpu);
            host.FreeMemoryMiB = Math.Max(0, (long)Math.Floor(memory * keep) - used);
        }

        return host;
    }

    /// <summary>
    /// A Proxmox node is online by its status; a vSphere host when it is
    /// connected and powered on. A host that does not say is taken to be online.
    /// </summary>
    private static bool Online(string hypervisor, InventoryEntry entry)
    {
        var properties = entry.Properties ?? [];

        if (hypervisor == InfrastructureProviders.Proxmox)
            return !properties.TryGetValue("status", out var status) || string.Equals(status, "online", StringComparison.OrdinalIgnoreCase);

        var connected = !properties.TryGetValue("connectionState", out var state) || string.Equals(state, "connected", StringComparison.OrdinalIgnoreCase);
        var powered = !properties.TryGetValue("powerState", out var power) || string.Equals(power, "poweredOn", StringComparison.OrdinalIgnoreCase);
        return connected && powered;
    }

    /// <summary>
    /// A Proxmox storage that holds no VM or container disks (an ISO or backup
    /// store) is no room for one; nor is a vSphere datastore that isn't accessible.
    /// </summary>
    private static bool Usable(string hypervisor, InventoryEntry entry)
    {
        var properties = entry.Properties ?? [];

        if (hypervisor == InfrastructureProviders.Proxmox)
        {
            return !properties.TryGetValue("content", out var content) ||
                content.Split(',', StringSplitOptions.TrimEntries).Any(x => x is "images" or "rootdir");
        }

        return !properties.TryGetValue("accessible", out var accessible) || accessible == "true";
    }

    private static void Track(HypervisorRoom room, string connection, string what, InventoryCategoryResult result)
    {
        if (result.LastUpdated is { } read && (room.ReadAt == null || read < room.ReadAt))
            room.ReadAt = read;

        if (result.Stale)
            room.Notes.Add($"The {what} of {connection} could not be read just now; the room is from an earlier read.");
    }

    private static long? Long(InventoryEntry entry, string name) =>
        entry.Properties != null && entry.Properties.TryGetValue(name, out var text) &&
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? (long)value
            : null;

    private static double? Double(InventoryEntry entry, string name) =>
        entry.Properties != null && entry.Properties.TryGetValue(name, out var text) &&
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
