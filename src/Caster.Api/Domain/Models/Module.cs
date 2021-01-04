// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Caster.Api.Infrastructure.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Caster.Api.Domain.Models
{
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
        public List<string> Outputs { get; set; } = new List<string>();

    }

    public class ModuleVariable
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string VariableType { get; set; }
        public string DefaultValue { get; set; }
        public bool IsOptional
        {
            get
            {
                return this.DefaultValue != null;
            }
        }
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
                    v => JsonSerializer.Deserialize<List<string>>(v, DefaultJsonSettings.Settings));

        }

    }

}
