// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Caster.Api.Features.Modules
{
    // modules must be uniquely identified in terraform cloud/enterprise
    // <ORGANIZATION>/<MODULE NAME>/<PROVIDER>
    // this is stored in Path

    public class ModuleSummary
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string Description { get; set; }
    }

    [DataContract(Name="ModuleModel")]
    public class Module
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string Description { get; set; }
        public List<ModuleVersion> Versions { get; set; } = new List<ModuleVersion>();
        public DateTime? DateModified { get; set; }
    }

    public class ModuleVersion
    {
        public Guid Id { get; set; }
        public Guid ModuleId { get; set; }
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
        public bool IsOptional { get; set; }
    }
}
