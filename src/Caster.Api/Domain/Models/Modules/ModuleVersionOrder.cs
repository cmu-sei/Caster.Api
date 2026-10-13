// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Caster.Api.Domain.Models;

/// <summary>
/// Orders module versions the way a user reads their tags: highest version first.
/// The import date is not shown anywhere, so it only breaks ties and orders tags
/// that are not version numbers (such as "main"), which come last, newest first.
/// </summary>
public static class ModuleVersionOrder
{
    public static List<T> HighestFirst<T>(IEnumerable<T> versions, Func<T, string> name, Func<T, DateTime> dateCreated)
    {
        return versions
            .Select(v => (Item: v, Key: ParsedVersion.Parse(name(v))))
            .OrderBy(x => x.Key == null)
            .ThenByDescending(x => x.Key?.Core)
            .ThenBy(x => x.Key?.PreRelease != null)
            .ThenByDescending(x => x.Key?.PreRelease, StringComparer.Ordinal)
            .ThenByDescending(x => dateCreated(x.Item))
            .Select(x => x.Item)
            .ToList();
    }

    public static List<ModuleVersion> HighestFirst(IEnumerable<ModuleVersion> versions) =>
        HighestFirst(versions, v => v.Name, v => v.DateCreated);

    private record ParsedVersion(Version Core, string PreRelease)
    {
        // Accepts "1", "1.2", "1.2.3" or "1.2.3.4", with an optional leading v,
        // pre-release ("-rc1") and build metadata ("+abc", ignored). A
        // per-module tag's "module/" prefix is ignored, so
        // "network-segment/v1.10.0" sorts above "network-segment/v1.2.0".
        public static ParsedVersion Parse(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            var text = name.Trim();
            var slash = text.LastIndexOf('/');
            if (slash >= 0)
                text = text[(slash + 1)..];
            if (text.StartsWith('v') || text.StartsWith('V'))
                text = text[1..];

            var plus = text.IndexOf('+');
            if (plus >= 0)
                text = text[..plus];

            string preRelease = null;
            var dash = text.IndexOf('-');
            if (dash >= 0)
            {
                preRelease = text[(dash + 1)..];
                text = text[..dash];
            }

            var parts = text.Split('.');
            if (parts.Length is < 1 or > 4)
                return null;

            var numbers = new int[4];
            for (var i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], out numbers[i]) || numbers[i] < 0)
                    return null;
            }

            // Pad to four parts so "1.2" and "1.2.0" compare equal.
            return new ParsedVersion(new Version(numbers[0], numbers[1], numbers[2], numbers[3]), preRelease);
        }
    }
}
