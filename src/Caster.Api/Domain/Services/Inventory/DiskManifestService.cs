// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Caster.Api.Domain.Models;

namespace Caster.Api.Domain.Services.Inventory;

public interface IDiskManifestService
{
    /// <summary>
    /// Lists the VM templates and ISOs in the inventory that <paramref name="files"/> refer to.
    /// Reads only the cached inventory, so it never waits on a provider.
    /// </summary>
    DiskManifest Build(IEnumerable<ArchiveFileEntry> files);

    /// <summary>
    /// Checks each disk in <paramref name="manifest"/> against this system's inventory.
    /// </summary>
    List<ImportedDisk> Check(DiskManifest manifest);
}

/// <summary>
/// Caster does not know which attribute of which Terraform resource holds a disk, and
/// providers disagree. It does know every template and ISO name the inventory discovered,
/// so a quoted string in a file that is exactly one of those names is a reference to it.
/// </summary>
public partial class DiskManifestService(IInventoryService inventoryService) : IDiskManifestService
{
    private static readonly (DiskKind Kind, InventoryCategory Category, string Label)[] Categories =
    [
        (DiskKind.VmTemplate, InventoryCategory.VmTemplate, "VM templates"),
        (DiskKind.Iso, InventoryCategory.Iso, "ISOs")
    ];

    public DiskManifest Build(IEnumerable<ArchiveFileEntry> files)
    {
        var manifest = new DiskManifest();
        var catalog = ReadCatalog(manifest.Warnings);

        if (catalog.Count == 0)
            return manifest;

        var found = new Dictionary<(DiskKind, string, string), DiskManifestEntry>();

        foreach (var file in files)
        {
            foreach (var literal in StringLiterals(file.Content).Distinct(StringComparer.Ordinal))
            {
                if (!catalog.TryGetValue(literal, out var matches))
                    continue;

                foreach (var match in matches)
                {
                    var key = (match.Kind, match.Connection, match.Entry.Id ?? match.Entry.Name);

                    if (!found.TryGetValue(key, out var disk))
                    {
                        disk = new DiskManifestEntry
                        {
                            Kind = match.Kind,
                            Name = match.Entry.Name,
                            Path = match.Entry.Path,
                            Connection = match.Connection,
                            Provider = match.Provider,
                            Properties = new Dictionary<string, string>(match.Entry.Properties ?? [])
                        };
                        found[key] = disk;
                    }

                    if (!disk.ReferencedBy.Contains(file.Path, StringComparer.Ordinal))
                        disk.ReferencedBy.Add(file.Path);
                }
            }
        }

        manifest.Disks = found.Values
            .OrderBy(x => x.Kind)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Connection, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return manifest;
    }

    public List<ImportedDisk> Check(DiskManifest manifest)
    {
        if (manifest?.Disks == null || manifest.Disks.Count == 0)
            return [];

        // Only categories that were actually read can say a disk is missing.
        var readable = new HashSet<DiskKind>();
        var catalog = ReadCatalog(null, readable);

        return manifest.Disks
            .GroupBy(x => (x.Kind, x.Name))
            .Select(group =>
            {
                var (kind, name) = group.Key;
                var status = DiskStatus.Unknown;

                if (catalog.TryGetValue(name ?? string.Empty, out var matches) && matches.Any(x => x.Kind == kind))
                    status = DiskStatus.Present;
                else if (readable.Contains(kind))
                    status = DiskStatus.Missing;

                return new ImportedDisk
                {
                    Kind = kind,
                    Name = name,
                    Status = status,
                    ReferencedBy = group.SelectMany(x => x.ReferencedBy ?? []).Distinct(StringComparer.Ordinal).ToList()
                };
            })
            .ToList();
    }

    private record CatalogMatch(DiskKind Kind, string Connection, string Provider, InventoryEntry Entry);

    /// <summary>
    /// Every template and ISO on every enabled connection, keyed by both name and path.
    /// </summary>
    private Dictionary<string, List<CatalogMatch>> ReadCatalog(List<string> warnings, HashSet<DiskKind> readable = null)
    {
        var catalog = new Dictionary<string, List<CatalogMatch>>(StringComparer.Ordinal);
        var connections = inventoryService.GetConnections().Where(x => x.Enabled).ToArray();

        if (connections.Length == 0)
        {
            warnings?.Add("Infrastructure inventory is not enabled, so no VM templates or ISOs could be identified.");
            return catalog;
        }

        foreach (var connection in connections)
        {
            var snapshot = inventoryService.GetSnapshot(connection.Name);

            foreach (var (kind, category, label) in Categories)
            {
                var result = snapshot?.Get(category);

                if (result == null || !result.Available)
                {
                    warnings?.Add($"{label} on connection '{connection.Name}' could not be read, so any it holds are not listed. {result?.Error}".TrimEnd());
                    continue;
                }

                readable?.Add(kind);

                foreach (var entry in result.Entries)
                {
                    var match = new CatalogMatch(kind, connection.Name, connection.Provider, entry);

                    foreach (var value in new[] { entry.Name, entry.Path }.Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.Ordinal))
                    {
                        if (!catalog.TryGetValue(value, out var list))
                            catalog[value] = list = [];

                        list.Add(match);
                    }
                }
            }
        }

        return catalog;
    }

    /// <summary>
    /// The double quoted strings in HCL or JSON, unescaped. Strings that interpolate are
    /// skipped, since their value is only known at plan time.
    /// </summary>
    internal static IEnumerable<string> StringLiterals(string content)
    {
        if (string.IsNullOrEmpty(content))
            yield break;

        foreach (Match match in QuotedString().Matches(content))
        {
            var raw = match.Groups[1].Value;

            if (raw.Length == 0 || raw.Contains("${", StringComparison.Ordinal) || raw.Contains("%{", StringComparison.Ordinal))
                continue;

            yield return Unescape(raw);
        }
    }

    private static string Unescape(string raw)
    {
        if (!raw.Contains('\\'))
            return raw;

        var builder = new StringBuilder(raw.Length);

        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '\\' && i + 1 < raw.Length)
            {
                i++;
                builder.Append(raw[i] switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    _ => raw[i]
                });
            }
            else
            {
                builder.Append(raw[i]);
            }
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"""((?:[^""\\\r\n]|\\.)*)""")]
    private static partial Regex QuotedString();
}
