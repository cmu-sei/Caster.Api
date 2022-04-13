// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using Caster.Api.Features.DesignModules;

namespace Caster.Api.Features.Designs;

public class Design
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public Guid DirectoryId { get; set; }
    public List<DesignModule> Modules { get; set; }
    public bool Enabled { get; set; }
}