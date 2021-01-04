// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Caster.Api.Infrastructure.Serialization;

namespace Caster.Api.Domain.Models
{
    public class State
    {
        public TFResource[] Resources { get; set; } = new TFResource[0];

        public Resource[] GetResources()
        {
            var resources = new List<Resource>();

            foreach (var res in this.Resources.Where(r => r.Mode == "managed"))
            {
                foreach (var instance in res.Instances)
                {
                    var index = "";

                    if (instance.Index_Key != null)
                    {
                        if (double.TryParse(instance.Index_Key, out double result))
                        {
                            index = $"[{instance.Index_Key}]";
                        }
                        else
                        {
                            index = $"[\"{instance.Index_Key}\"]";
                        }
                    }

                    var resource = new Resource
                    {
                        Attributes = instance.Attributes,
                        Address = $"{res.Address}{index}",
                        BaseAddress = $"{res.Address}",
                        Id = instance.Attributes.GetProperty("id").GetString(),
                        Name = instance.Attributes.TryGetProperty("name", out JsonElement element) ? element.GetString() : res.Name,
                        Tainted = instance.Status == "tainted",
                        Type = res.Type
                    };

                    resources.Add(resource);
                }
            }

            return resources.ToArray();
        }
    }

    public class TFResource
    {
        public string Mode { get; set; }
        public string Type { get; set; }
        public string Name { get; set; }
        public string Provider { get; set; }
        public string Module { get; set; } = string.Empty;

        public string Address
        {
            get
            {
                var module = string.IsNullOrEmpty(this.Module) ?
                    string.Empty : $"{this.Module}.";

                return $"{module}{this.Type}.{this.Name}";
            }
        }

        public Instance[] Instances { get; set; }
    }

    public class Instance
    {
        [JsonConverter(typeof(NumberToStringConverter))]
        public string Index_Key { get; set; }
        public string Status { get; set; }
        public JsonElement Attributes { get; set; }
    }
}

