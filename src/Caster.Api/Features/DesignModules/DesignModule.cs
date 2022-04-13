// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;

namespace Caster.Api.Features.DesignModules;

public class DesignModule
{
    public Guid Id { get; set; }
    public Guid DesignId { get; set; }
    public Guid ModuleId { get; set; }
    public string Name { get; set; }
    public string ModuleVersion { get; set; }
    public bool Enabled { get; set; }
    public ICollection<ModuleValue> Values
    {
        get
        {
            return ModuleValue.GetModuleValues(Domain.Models.DesignModule.GetValues(ValuesJson));
        }
    }
    private string ValuesJson { get; set; }
}

public class ModuleValue
{
    public string Name { get; set; }
    public string Value { get; set; }

    public ModuleValue() { }

    public ModuleValue(Domain.Models.ModuleValue moduleValue)
    {
        Name = moduleValue.Name;
        Value = moduleValue.Value;
    }

    public Domain.Models.ModuleValue ToDomain()
    {
        return new Domain.Models.ModuleValue
        {
            Name = Name,
            Value = Value
        };
    }

    public static ICollection<ModuleValue> GetModuleValues(IEnumerable<Domain.Models.ModuleValue> domainModuleValues)
    {
        if (domainModuleValues == null)
            return null;

        var values = new List<ModuleValue>();

        foreach (var domainModuleValue in domainModuleValues)
        {
            values.Add(new ModuleValue(domainModuleValue));
        }

        return values;
    }

    public static ICollection<Domain.Models.ModuleValue> ToDomain(IEnumerable<ModuleValue> moduleValues)
    {
        var domainValues = new List<Domain.Models.ModuleValue>();

        foreach (var moduleValue in moduleValues)
        {
            domainValues.Add(moduleValue.ToDomain());
        }

        return domainValues;
    }
}
