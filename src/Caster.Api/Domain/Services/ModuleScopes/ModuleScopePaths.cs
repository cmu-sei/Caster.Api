// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Caster.Api.Domain.Models;

namespace Caster.Api.Domain.Services.ModuleScopes;

/// <summary>
/// The paths of a Module Scope. A Module's Path is <c>&lt;source&gt;</c> or
/// <c>&lt;source&gt;/&lt;folder&gt;</c> (see the module sources), and an entry allows the Modules at
/// exactly that Path and those below it: <c>cmu</c> allows <c>cmu</c> and <c>cmu/exercise-x</c>, not
/// <c>cmu2</c>. Matching is exact in case, as the Paths are stored.
/// </summary>
public static class ModuleScopePaths
{
    public const int MaximumPaths = 100;
    public const int MaximumLength = 300;

    private static readonly Regex Segment = new(@"^[A-Za-z0-9][A-Za-z0-9._ \-]*$", RegexOptions.Compiled);

    /// <summary>
    /// The path trimmed of white space and of slashes at either end, or null when it is not a path:
    /// empty, too long, with an empty, dot or ".." segment, or a character that no source name or
    /// folder has (wildcards, backslashes).
    /// </summary>
    public static string Normalize(string path)
    {
        var trimmed = path?.Trim().Trim('/').Trim();

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaximumLength)
            return null;

        var segments = trimmed.Split('/');
        return segments.All(x => x.Length > 0 && x != "." && x != ".." && x.Trim() == x && Segment.IsMatch(x)) ? trimmed : null;
    }

    /// <summary>Whether a Module at <paramref name="modulePath"/> is allowed by any of <paramref name="allowed"/>.</summary>
    public static bool Matches(IEnumerable<string> allowed, string modulePath) =>
        modulePath is not null &&
        allowed.Any(x => modulePath == x || modulePath.StartsWith(x + "/", StringComparison.Ordinal));

    /// <summary>The same test as <see cref="Matches"/>, as a query condition.</summary>
    public static Expression<Func<Module, bool>> Allows(IReadOnlyCollection<string> allowed)
    {
        var module = Expression.Parameter(typeof(Module), "module");
        var path = Expression.Property(module, nameof(Module.Path));
        var startsWith = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)]);

        Expression body = Expression.Constant(false);

        foreach (var entry in allowed.Distinct())
        {
            var equal = Expression.Equal(path, Expression.Constant(entry));
            var below = Expression.Call(path, startsWith, Expression.Constant(entry + "/"));
            body = Expression.OrElse(body, Expression.OrElse(equal, below));
        }

        return Expression.Lambda<Func<Module, bool>>(body, module);
    }
}

/// <summary>
/// The Modules a project may use: every one, or those matching <see cref="Paths"/>. The union of
/// the scopes assigned to the project and to its project group.
/// </summary>
public sealed class ModuleAccess
{
    public static readonly ModuleAccess Everything = new(false, []);

    private ModuleAccess(bool restricted, string[] paths)
    {
        Restricted = restricted;
        Paths = paths;
    }

    /// <summary>False when no scope is assigned: every Module is allowed.</summary>
    public bool Restricted { get; }

    public string[] Paths { get; }

    public static ModuleAccess Of(IEnumerable<string> paths) =>
        new(true, paths.Distinct().Order(StringComparer.Ordinal).ToArray());

    public bool Allows(string modulePath) => !Restricted || ModuleScopePaths.Matches(Paths, modulePath);

    /// <summary>
    /// The access of someone who can use the Modules of several projects: everything if any one of
    /// them is unrestricted, else the union (nothing at all for no projects).
    /// </summary>
    public static ModuleAccess Union(IEnumerable<ModuleAccess> accesses)
    {
        var list = accesses.ToList();
        return list.Any(x => !x.Restricted) ? Everything : Of(list.SelectMany(x => x.Paths));
    }

    public System.Linq.IQueryable<Module> Apply(System.Linq.IQueryable<Module> modules) =>
        Restricted ? modules.Where(ModuleScopePaths.Allows(Paths)) : modules;
}
