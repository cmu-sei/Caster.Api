// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Text.Json.Serialization;

namespace Caster.Api.Domain.Models;

public class ModuleVariable
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string VariableType { get; set; }
    public string DefaultValue { get; set; }

    [JsonIgnore]
    public bool IsOptional
    {
        get
        {
            return this.DefaultValue != null;
        }
    }

    [JsonIgnore]
    public bool RequiresQuotes
    {
        get
        {
            if (this.VariableType == null)
            {
                return false;
            }

            return this.VariableType.ToLower() == "string";
        }
    }
}

