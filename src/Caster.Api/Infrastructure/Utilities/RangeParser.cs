// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;

namespace Caster.Api.Infrastructure.Utilities;

public static class IdParser
{
    public static int[] ParseIds(string[] ids)
    {
        if (ids == null)
            return Array.Empty<int>();

        return ids
            .SelectMany(r =>
                r.Contains('-')
                    ? CreateRange(r)
                    : new[] { int.Parse(r) })
            .ToArray();
    }

    private static int[] CreateRange(string rangeStr)
    {
        var parts = rangeStr.Split('-');
        var start = int.Parse(parts[0]);
        var end = int.Parse(parts[1]);
        return Enumerable.Range(start, end - start + 1).ToArray();
    }
}