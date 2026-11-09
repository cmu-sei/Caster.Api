// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Caster.Api.Infrastructure.Options;

namespace Caster.Api.Domain.Services.Inventory;

/// <summary>
/// The per-connection Inventory:Resources settings: which kinds of object are read,
/// and the Locations, Include and Exclude filters on each.
/// </summary>
public static class InventoryFilters
{
    /// <summary>A name filter that takes longer than this is reported, not waited on.</summary>
    internal static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    private const RegexOptions PatternOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    public static readonly InventoryCategory[] Categories =
    [
        InventoryCategory.VmTemplate,
        InventoryCategory.Iso,
        InventoryCategory.Network,
        InventoryCategory.Datastore,
        InventoryCategory.ContentLibrary,
        InventoryCategory.Host
    ];

    /// <summary>The settings key for a category, e.g. "VmTemplates".</summary>
    public static string Key(InventoryCategory category) => category switch
    {
        InventoryCategory.VmTemplate => nameof(InventoryResourcesOptions.VmTemplates),
        InventoryCategory.Iso => nameof(InventoryResourcesOptions.Isos),
        InventoryCategory.Network => nameof(InventoryResourcesOptions.Networks),
        InventoryCategory.Datastore => nameof(InventoryResourcesOptions.Datastores),
        InventoryCategory.ContentLibrary => nameof(InventoryResourcesOptions.ContentLibraries),
        InventoryCategory.Host => nameof(InventoryResourcesOptions.Hosts),
        _ => category.ToString()
    };

    public static InventoryResourceOptions Settings(InfrastructureConnectionOptions options, InventoryCategory category)
    {
        var resources = options?.Inventory?.Resources ?? new InventoryResourcesOptions();

        return category switch
        {
            InventoryCategory.VmTemplate => resources.VmTemplates,
            InventoryCategory.Iso => resources.Isos,
            InventoryCategory.Network => resources.Networks,
            InventoryCategory.Datastore => resources.Datastores,
            InventoryCategory.ContentLibrary => resources.ContentLibraries,
            InventoryCategory.Host => resources.Hosts,
            _ => null
        } ?? new InventoryResourceOptions();
    }

    public static bool IsEnabled(InfrastructureConnectionOptions options, InventoryCategory category) =>
        Settings(options, category).Enabled;

    /// <summary>The categories that are read, in <see cref="Categories"/> order.</summary>
    public static InventoryCategory[] Enabled(InfrastructureConnectionOptions options) =>
        Categories.Where(x => IsEnabled(options, x)).ToArray();

    /// <summary>
    /// Why the connection's settings can't be used, or null when they can: a filter that
    /// isn't a valid regular expression, a location in the wrong form for the provider,
    /// or a setting the provider doesn't have. Never echoes a credential.
    /// </summary>
    public static string Validate(InfrastructureConnectionOptions options)
    {
        var problems = new List<string>();
        var vsphere = IsVsphere(options);

        if (!vsphere && (options.Datacenters ?? []).Any(x => !string.IsNullOrWhiteSpace(x)))
            problems.Add("Infrastructure:Datacenters is only for vsphere connections.");

        foreach (var category in Categories)
        {
            var key = $"Infrastructure:Inventory:Resources:{Key(category)}";
            var settings = Settings(options, category);
            var locations = Locations(settings);

            foreach (var (name, pattern) in new[] { ("Include", settings.Include), ("Exclude", settings.Exclude) })
            {
                if (string.IsNullOrWhiteSpace(pattern))
                    continue;

                try
                {
                    _ = new Regex(pattern, PatternOptions, MatchTimeout);
                }
                catch (ArgumentException ex)
                {
                    problems.Add($"{key}:{name} is not a valid regular expression: {ex.Message}");
                }
            }

            if (locations.Length == 0)
                continue;

            if (category is InventoryCategory.Iso)
            {
                problems.Add($"{key}:Locations doesn't apply. ISOs are found in Infrastructure:IsoLocations.");
                continue;
            }

            if (category is InventoryCategory.ContentLibrary)
            {
                problems.Add($"{key}:Locations doesn't apply to content libraries. Use Include and Exclude.");
                continue;
            }

            if (vsphere)
            {
                // Networks also take a distributed switch's name.
                var wrong = locations.Where(x => !x.StartsWith('/') && category != InventoryCategory.Network).ToArray();

                if (wrong.Length > 0)
                    problems.Add($"{key}:Locations names {Quoted(wrong)}. Each entry is a full inventory path, e.g. \"/DC1/vm/Templates\".");
            }
            else
            {
                var wrong = locations.Where(x => x.Contains('/')).ToArray();

                if (wrong.Length > 0)
                    problems.Add($"{key}:Locations names {Quoted(wrong)}. Each entry is a name, e.g. a node, with no '/'.");
            }
        }

        return problems.Count == 0 ? null : string.Join(" ", problems);
    }

    /// <summary>
    /// Leaves out the entries the category's filters don't list, and reports a category
    /// that's turned off as not enabled. Assumes <see cref="Validate"/> passed.
    /// </summary>
    public static InventorySnapshot Apply(InventorySnapshot snapshot, InfrastructureConnectionOptions options)
    {
        if (snapshot is null)
            return null;

        snapshot.VmTemplates = Apply(snapshot.VmTemplates, options, InventoryCategory.VmTemplate);
        snapshot.Isos = Apply(snapshot.Isos, options, InventoryCategory.Iso);
        snapshot.Networks = Apply(snapshot.Networks, options, InventoryCategory.Network);
        snapshot.Datastores = Apply(snapshot.Datastores, options, InventoryCategory.Datastore);
        snapshot.ContentLibrary = Apply(snapshot.ContentLibrary, options, InventoryCategory.ContentLibrary);
        snapshot.Hosts = Apply(snapshot.Hosts, options, InventoryCategory.Host);

        return snapshot;
    }

    private static InventoryCategoryResult Apply(InventoryCategoryResult result, InfrastructureConnectionOptions options, InventoryCategory category)
    {
        // A provider without this kind of object says so, whatever the settings.
        if (result is { Applies: false })
            return result;

        if (!IsEnabled(options, category))
            return InventoryCategoryResult.NotEnabled(InventoryMessages.CategoryNotEnabled(category, Key(category)));

        if (result is not { Available: true })
            return result;

        var filter = For(options, category);

        try
        {
            result.Entries = result.Entries.Where(filter.Matches).ToArray();
        }
        catch (RegexMatchTimeoutException)
        {
            return InventoryCategoryResult.Unavailable(
                $"Infrastructure:Inventory:Resources:{Key(category)}'s Include or Exclude took too long to match, so the list isn't shown.");
        }

        return result;
    }

    /// <summary>The filter for one category, for a provider to apply before costly reads.</summary>
    public static Filter For(InfrastructureConnectionOptions options, InventoryCategory category)
    {
        var settings = Settings(options, category);

        return new Filter(
            Locations(settings),
            Pattern(settings.Include),
            Pattern(settings.Exclude));
    }

    private static Regex Pattern(string pattern) =>
        string.IsNullOrWhiteSpace(pattern) ? null : new Regex(pattern, PatternOptions, MatchTimeout);

    private static string[] Locations(InventoryResourceOptions settings) =>
        (settings.Locations ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Select(x => x.Length > 1 ? x.TrimEnd('/') : x)
            .ToArray();

    private static bool IsVsphere(InfrastructureConnectionOptions options) =>
        string.Equals(options?.Provider?.Trim(), InfrastructureProviders.Vsphere, StringComparison.OrdinalIgnoreCase);

    private static string Quoted(IEnumerable<string> values) => string.Join(", ", values.Select(x => $"\"{x}\""));

    /// <summary>
    /// One category's Locations, Include and Exclude.
    /// </summary>
    public sealed class Filter(string[] locations, Regex include, Regex exclude)
    {
        public bool Matches(InventoryEntry entry) =>
            Matches(entry.Name, entry.Locations);

        /// <summary>
        /// Whether an object with this name, at these locations, is listed. A location
        /// that starts with '/' is a path, matching the same path or one below it; any
        /// other is a name. Both ignore case.
        /// </summary>
        public bool Matches(string name, IEnumerable<string> at)
        {
            if (locations.Length > 0)
            {
                var keys = (at ?? []).Where(x => !string.IsNullOrEmpty(x)).ToArray();

                if (!locations.Any(location => keys.Any(key => IsAt(key, location))))
                    return false;
            }

            name ??= string.Empty;

            if (include is not null && !include.IsMatch(name))
                return false;

            return exclude is null || !exclude.IsMatch(name);
        }

        private static bool IsAt(string key, string location)
        {
            if (!location.StartsWith('/'))
                return string.Equals(key, location, StringComparison.OrdinalIgnoreCase);

            if (location == "/")
                return key.StartsWith('/');

            return string.Equals(key, location, StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith(location + "/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
