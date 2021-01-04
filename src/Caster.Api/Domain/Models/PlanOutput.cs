// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Domain.Models
{
    public class PlanOutput
    {
        [JsonPropertyName("resource_changes")]
        public ResourceChange[] ResourceChanges { get; set; }

        public ResourceChange[] GetAddedMachines()
        {
            if (ResourceChanges == null) return new ResourceChange[] {};
            return ResourceChanges.Where(r => r.Type == "vsphere_virtual_machine" && r.Change.Actions.Contains(ChangeType.Create)).ToArray();
        }
    }

    public class ResourceChange
    {
        public string Address { get; set; }
        [JsonPropertyName("module_address")]
        public string ModuleAddress { get; set; }
        public string Name { get; set; }
        public string Mode { get; set; }
        public string Type { get; set; }
        public string Deposed { get; set; }
        public Change Change { get; set; }
    }

    public class Change
    {
        public ChangeType[] Actions { get; set; }
    }

    [JsonConverter(typeof(JsonStringEnumMemberConverter))]
    public enum ChangeType
    {
        [EnumMember(Value = "no-op")]
        Noop,
        Create,
        Read,
        Update,
        Delete
    }
}
