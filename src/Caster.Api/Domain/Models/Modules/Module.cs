// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Domain.Models;

[Index(nameof(SourceName))]
public class Module : IEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Path { get; set; }
    public string Description { get; set; }
    public virtual ICollection<ModuleVersion> Versions { get; set; } = new List<ModuleVersion>();
    public DateTime? DateModified { get; set; }

    /// <summary>
    /// The git module source this Module was read from, lowercased because
    /// source names are case-insensitive (which also lets the index serve an
    /// equality match), or null for a Gitlab Module or one stored before
    /// sources were recorded. Paths alone cannot
    /// tell: a git source "terraform" and a Gitlab project "terraform/vpc"
    /// share the prefix.
    /// </summary>
    public string SourceName { get; set; }

    /// <summary>
    /// When a whole-source read of a git module source first found this Module
    /// gone from it, or null while it is in its source. Never set for Gitlab
    /// Modules, which are added by hand per project. Nothing deletes a Module
    /// because of this; it only tells an administrator which are leftovers.
    /// </summary>
    public DateTime? MissingSince { get; set; }
}