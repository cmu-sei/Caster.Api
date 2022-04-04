// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text.Json;
using Caster.Api.Infrastructure.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Caster.Api.Domain.Models;

public class Design
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }
    public string Name { get; set; }
    public Guid DirectoryId { get; set; }
    public virtual Directory Directory { get; set; }
    public bool Enabled { get; set; } = true;
    public virtual ICollection<DesignModule> Modules { get; set; } = new List<DesignModule>();
    public virtual ICollection<Variable> Variables { get; set; } = new List<Variable>();
}

public class ModuleValue
{
    public string Name { get; set; }
    public string Value { get; set; }
}

public class DesignModule
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }
    public Guid DesignId { get; set; }
    public virtual Design Design { get; set; }
    public Guid ModuleId { get; set; }
    public virtual Module Module { get; set; }
    public string Name { get; set; }
    public string ModuleVersion { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Split into 2 properties rather than using a value converter to be compatible
    /// with ProjectTo for use with ExplicitExpansion
    /// </summary>
    [NotMapped]
    public ICollection<ModuleValue> Values
    {
        get
        {
            return GetValues();
        }

        set
        {
            ValuesJson = JsonSerializer.Serialize(value, DefaultJsonSettings.Settings);
        }
    }

    private string ValuesJson { get; set; }

    /// <summary>
    /// Adds or updates the ModuleValues specified. 
    /// Will remove a ModuleValue if it's Value is set to null or empty.
    /// Will not change ModuleValues that are not specified.
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    public ICollection<ModuleValue> AddOrUpdateValues(ICollection<ModuleValue> values)
    {
        var currentValues = Values;

        foreach (var val in values)
        {
            var currentVal = currentValues
                .Where(x => x.Name == val.Name)
                .FirstOrDefault();

            if (currentVal == null)
            {
                // Add value if it doesn't exist
                // If no new value, do nothing since it already didn't exist
                if (!string.IsNullOrEmpty(val.Value))
                {
                    currentValues.Add(val);
                }
            }
            else
            {
                if (string.IsNullOrEmpty(val.Value))
                {
                    // remove existing if new Value is null or empty
                    currentValues.Remove(currentVal);
                }
                else
                {
                    // update existing if new Value is not null or empty
                    currentVal.Value = val.Value;
                }
            }
        }

        Values = currentValues;
        return currentValues;
    }

    public static ICollection<ModuleValue> GetValues(string valuesJson)
    {
        if (valuesJson == String.Empty)
        {
            return new List<ModuleValue>();
        }
        else if (valuesJson == null)
        {
            return null;
        }
        else
        {
            return JsonSerializer.Deserialize<List<ModuleValue>>(valuesJson, DefaultJsonSettings.Settings);
        }
    }

    public ICollection<ModuleValue> GetValues()
    {
        return DesignModule.GetValues(ValuesJson);
    }
}

public class DesignModuleConfiguration : IEntityTypeConfiguration<DesignModule>
{
    public void Configure(EntityTypeBuilder<DesignModule> builder)
    {
        builder.Property<string>("ValuesJson").HasColumnName("ValuesJson");
    }
}