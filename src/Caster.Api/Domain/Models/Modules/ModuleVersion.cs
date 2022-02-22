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

public class ModuleVersion
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