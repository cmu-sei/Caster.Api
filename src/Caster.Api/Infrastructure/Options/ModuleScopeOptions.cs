// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;

namespace Caster.Api.Infrastructure.Options;

/// <summary>
/// Deployment settings for Module Scopes, under <c>Terraform:ModuleScopes</c>. Which sources every
/// project may use is set on the source itself (<see cref="ModuleSourceOptions.Shared"/>).
/// </summary>
public class ModuleScopeOptions
{
    /// <summary>
    /// When true, a project with no Module Scope (directly or through its project group) can use
    /// only the shared sources, instead of every Module. Off by default so existing deployments
    /// keep working until their projects are scoped.
    /// </summary>
    public bool RestrictUnassigned { get; set; }
}

public static class ModuleScopeOptionsExtensions
{
    /// <summary>
    /// The names of the configured sources every project may use, as they prefix a Module's Path.
    /// An entry with no name or no url is ignored, as everywhere else, and a name listed twice
    /// (compared case insensitively, as source names are) counts once.
    /// </summary>
    public static string[] SharedSourceNames(this TerraformOptions options) =>
        (options?.ModuleSources ?? [])
            .Where(x => x is { Shared: true, IsConfigured: true })
            .Select(x => x.Name)
            .DistinctBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();
}
