// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Caster.Api.Infrastructure.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Caster.Api.Domain.Models;

public class ModuleVersion : IEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }
    public Guid ModuleId { get; set; }
    public virtual Module Module { get; set; }
    public string Name { get; set; }
    public string UrlLink { get; set; }
    public DateTime DateCreated { get; set; }
    public List<ModuleVariable> Variables { get; set; } = new List<ModuleVariable>();
    public List<ModuleOutput> Outputs { get; set; } = new List<ModuleOutput>();

    /// <summary>
    /// Names of the required variables (those without a default) that have no value.
    /// </summary>
    public string[] MissingRequiredVariables(IEnumerable<ModuleValue> values)
    {
        var set = (values ?? [])
            .Where(x => !string.IsNullOrEmpty(x.Value))
            .Select(x => x.Name)
            .ToHashSet();

        return Variables
            .Where(v => !v.IsOptional && !set.Contains(v.Name))
            .Select(v => v.Name)
            .ToArray();
    }

    public string ToSnippet(string name, IEnumerable<ModuleValue> values)
    {
        var snippet = $"module \"{name}\" {{\n" +
                          $"  source = \"git::{UrlLink}?ref={Name}\"";

        foreach (var variable in values)
        {
            var v = Variables.Where(v => v.Name == variable.Name).SingleOrDefault();

            if (v == null)
            {
                continue;
            }

            var value = variable.Value;

            // Writing a blank required value as "" would count as set, so plan would
            // pass and the module would fail later. Left commented, Terraform reports
            // the missing argument and the user sees what still needs filling in.
            if (!v.IsOptional && string.IsNullOrEmpty(value))
            {
                snippet = $"{snippet}\n  # {variable.Name} = (required)";
                continue;
            }

            // A blank number, bool, list or map would be written as "name = ", which
            // isn't valid HCL. Leaving it out lets the variable's default apply.
            if (v.IsOptional && !v.RequiresQuotes && string.IsNullOrEmpty(value))
            {
                continue;
            }

            if (v.RequiresQuotes)
            {
                value = $"\"{value}\"";
            }

            snippet = $"{snippet}\n  {variable.Name} = {value}";
        }

        snippet = snippet + "\n}";
        return snippet;
    }

}

public class VersionConfiguration : IEntityTypeConfiguration<ModuleVersion>
{
    public void Configure(EntityTypeBuilder<ModuleVersion> builder)
    {
        builder
            .Property(p => p.Variables)
            .HasConversion(
                v => JsonSerializer.Serialize(v, DefaultJsonSettings.Settings),
                v => JsonSerializer.Deserialize<List<ModuleVariable>>(v, DefaultJsonSettings.Settings));

        builder
            .Property(p => p.Outputs)
            .HasConversion(
                v => JsonSerializer.Serialize(v, DefaultJsonSettings.Settings),
                v => JsonSerializer.Deserialize<List<ModuleOutput>>(v, DefaultJsonSettings.Settings));

    }
}