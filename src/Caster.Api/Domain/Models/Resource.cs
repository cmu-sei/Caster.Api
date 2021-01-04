// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Caster.Api.Domain.Models
{
    public class Resource
    {
        private static class ResourceTypes
        {
            public const string VsphereVirtualMachine = "vsphere_virtual_machine";
            public const string VsphereDistributedPortGroup = "vsphere_distributed_port_group";
            public const string VsphereHostPortGroup = "vsphere_host_port_group";
        }

        /// <summary>
        /// The Id of this Resource
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// The Name of this Resource
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The Terraform identifier for the type of this Resource
        /// e.g.!-- vsphere_virtual_machine, vsphere_virtual_network, etc --!
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// The Terraform Resource Address that references this Resource's Base Resource
        /// If this Resource was created with count > 1, using the Base Address as a target
        /// will target all of the resources created from it.
        /// BaseAddress will be null if the Resource was created with count = 1.
        /// </summary>
        public string BaseAddress { get; set; }

        /// <summary>
        /// The Terraform Resource Address that references this specific Resource
        /// Used for the targets list when creating a Run
        /// </summary>
        public string Address { get; set; }

        /// <summary>
        /// True if this Resource is tainted, meaning it will be destroyed and re-created on next Run
        /// </summary>
        public bool Tainted { get; set; }

        /// <summary>
        /// All of the primary attributes of this Resource
        /// </summary>
        public JsonElement Attributes { get; set; }

        public Dictionary<string, object> SearchableAttributes
        {
            get
            {
                return this.GetSearchable();
            }
        }

        private Dictionary<string, object> GetSearchable()
        {
            var dict = new Dictionary<string, object>();

            switch (this.Type)
            {
                case ResourceTypes.VsphereVirtualMachine:
                    {
                        if (this.Attributes.TryGetProperty("guest_ip_addresses", out JsonElement ipAddresses))
                            dict.Add("guest_ip_addresses", ipAddresses);

                        if (this.Attributes.TryGetProperty("guest_id", out JsonElement guestId))
                            dict.Add("guest_id", guestId);

                        break;
                    }
                case ResourceTypes.VsphereDistributedPortGroup:
                case ResourceTypes.VsphereHostPortGroup:
                    {
                        if (this.Attributes.TryGetProperty("vlan_id", out JsonElement vlanId))
                            dict.Add("vlan_id", vlanId);

                        break;
                    }
            }

            return dict;
        }

        public Guid[] GetTeamIds()
        {
            List<Guid> teamIds = null;

            // TODO: improve handling of this.
            if (this.Type == "vsphere_virtual_machine")
            {
                if (this.Attributes.TryGetProperty("extra_config", out JsonElement extraConfig))
                {
                    try
                    {
                        Dictionary<string, string> dict = JsonSerializer.Deserialize<Dictionary<string, string>>(extraConfig.ToString());

                        string[] teamIdKeywords = new string[] { "guestinfo.teamId", "guestinfo.team_id" };

                        foreach (var keyword in teamIdKeywords)
                        {
                            if (dict.ContainsKey(keyword))
                            {
                                if (teamIds == null)
                                {
                                    teamIds = new List<Guid>();
                                }

                                string idString = dict[keyword];
                                string[] ids = idString.Split(',');

                                foreach (var id in ids)
                                {
                                    Guid guid;
                                    if (Guid.TryParse(id, out guid))
                                    {
                                        teamIds.Add(guid);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {

                    }
                }
            }

            return teamIds == null ? null : teamIds.ToArray();
        }

        public bool IsVirtualMachine()
        {
            return this.Type == "vsphere_virtual_machine";
        }
    }
}
