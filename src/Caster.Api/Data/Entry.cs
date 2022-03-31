// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Caster.Api.Data;

public class Entry
{
    public Entry(EntityEntry entry)
    {
        Entity = entry.Entity;
        State = entry.State;
        Properties = entry.Properties;

        IsPropertyModified = new Dictionary<string, bool>();
        foreach (var prop in Properties)
        {
            IsPropertyModified[prop.Metadata.Name] = prop.IsModified;
        }
    }

    public string[] GetModifiedProperties()
    {
        return IsPropertyModified
            .Where(x => x.Value)
            .Select(x => x.Key)
            .ToArray();
    }

    public object Entity { get; set; }
    public EntityState State { get; set; }
    public IEnumerable<PropertyEntry> Properties { get; set; }
    private Dictionary<string, bool> IsPropertyModified { get; set; }
}
