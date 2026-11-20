// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;

namespace Caster.Api.Features.ModuleScopes;

/// <summary>A Module Scope as admins see it (ViewModules).</summary>
public class ModuleScope
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }

    /// <summary>The module sources and source folders it allows, as Paths: <c>cmu</c> or <c>cmu/exercise-x</c>.</summary>
    public string[] Paths { get; set; } = [];

    /// <summary>The projects it is assigned to directly.</summary>
    public ModuleScopeProject[] Projects { get; set; } = [];

    /// <summary>The project groups it is assigned to, reaching every project in each.</summary>
    public ModuleScopeProjectGroup[] ProjectGroups { get; set; } = [];
}

public class ModuleScopeProject
{
    public Guid ProjectId { get; set; }
    public string Name { get; set; }
}

public class ModuleScopeProjectGroup
{
    public Guid ProjectGroupId { get; set; }
    public string Name { get; set; }
}

/// <summary>
/// Which Modules a project may use, as the members of the project see it (ViewProject): every
/// one, or those at <see cref="Paths"/> plus the <see cref="SharedSources"/>.
/// </summary>
public class ProjectModuleAccess
{
    public Guid ProjectId { get; set; }

    /// <summary>
    /// False when every Module is allowed: no scope is assigned to the project or its group, and
    /// unassigned projects are not restricted (<c>Terraform:ModuleScopes:RestrictUnassigned</c>).
    /// </summary>
    public bool Restricted { get; set; }

    /// <summary>
    /// True when <see cref="Restricted"/> only because no scope is assigned and the deployment
    /// restricts unassigned projects: the project can use the shared sources alone.
    /// </summary>
    public bool RestrictedBecauseUnassigned { get; set; }

    /// <summary>
    /// The union of the paths of <see cref="Scopes"/>, sorted, without the shared sources. Empty
    /// when no scope applies.
    /// </summary>
    public string[] Paths { get; set; } = [];

    /// <summary>The names of the sources every project may use, on top of <see cref="Paths"/>.</summary>
    public string[] SharedSources { get; set; } = [];

    /// <summary>The scopes that apply, directly and through the project's group.</summary>
    public ProjectModuleAccessScope[] Scopes { get; set; } = [];

    /// <summary>Each of <see cref="Paths"/> with the scopes (and so the groups) it comes from.</summary>
    public ProjectModuleAccessPath[] PathOrigins { get; set; } = [];
}

public class ProjectModuleAccessPath
{
    public string Path { get; set; }

    /// <summary>The scopes that list the path.</summary>
    public ProjectModuleAccessScope[] Scopes { get; set; } = [];
}

public class ProjectModuleAccessScope
{
    public Guid ModuleScopeId { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string[] Paths { get; set; } = [];

    /// <summary>Whether the scope is assigned to the project itself; it can also come through its group.</summary>
    public bool IsDirect { get; set; }

    /// <summary>The project group the scope comes through, if it does.</summary>
    public ModuleScopeProjectGroup[] ProjectGroups { get; set; } = [];
}
