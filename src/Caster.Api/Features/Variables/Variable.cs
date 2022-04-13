// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Variables;

public class Variable
{
    public Guid Id { get; set; }
    public Guid DesignId { get; set; }
    public string Name { get; set; }
    public VariableType Type { get; set; }
    public string DefaultValue { get; set; }
    public string Terraform { get; set; }
}