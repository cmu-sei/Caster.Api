// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;

#nullable enable

namespace Caster.Api.Infrastructure.Extensions;

public static class EnumerableExtensions
{
    public static List<T> MergeBy<T>(this IEnumerable<T>? source, IEnumerable<T> overrides, Func<T, string> keySelector)
    {
        var overrideKeys = new HashSet<string>(overrides.Select(keySelector));
        return (source ?? Enumerable.Empty<T>())
            .Where(e => !overrideKeys.Contains(keySelector(e)))
            .Concat(overrides)
            .ToList();
    }
}
