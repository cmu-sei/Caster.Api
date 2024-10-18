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

public class Variable : IEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }
    public Guid DesignId { get; set; }
    public virtual Design Design { get; set; }
    public string Name { get; set; }
    public VariableType Type { get; set; }
    public string DefaultValue { get; set; }

    [NotMapped]
    public string Terraform
    {
        get
        {
            return $"${{var.{Name}}}"; // ${var.Name}
        }
    }

    public string ToSnippet()
    {
        return
        @$"variable {Name} {{
            type = {Type}
            {(DefaultValue != null ? "default = " + GetDefaultValueSnippet() : "")}
        }}";
    }

    /// <summary>
    /// Computes additional NotMapped modified properties  
    /// </summary>
    public string[] GetModifiedProperties(string[] modifiedProperties)
    {
        if (modifiedProperties.Contains(nameof(Name)))
        {
            var newList = new List<string> { nameof(Terraform) };
            newList.AddRange(modifiedProperties);
            return newList.ToArray();
        }
        else
        {
            return modifiedProperties;
        }
    }

    private string GetDefaultValueSnippet()
    {
        switch (Type)
        {
            case VariableType.@string:
                return @$"""{DefaultValue}""";
            default:
                return DefaultValue;
        }
    }
}

public enum VariableType
{
    @string,
    number,
    @bool,
    list,
    map
}