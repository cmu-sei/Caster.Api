// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Caster.Api.Domain.Models;

public class Module
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Path { get; set; }
    public string Description { get; set; }
    public virtual ICollection<ModuleVersion> Versions { get; set; } = new List<ModuleVersion>();
    public DateTime? DateModified { get; set; }
}