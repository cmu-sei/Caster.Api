// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Options;
using Xunit;

namespace Caster.Api.Tests.Domain.Models.Modules
{
    [Trait("Category", "Unit")]
    [Trait("Category", "Modules")]
    public class HypervisorsTests
    {
        [Theory]
        [InlineData(new[] { "bpg/proxmox" }, Hypervisors.Proxmox)]
        [InlineData(new[] { "telmate/proxmox", "hashicorp/random" }, Hypervisors.Proxmox)]
        [InlineData(new[] { "vmware/vsphere" }, Hypervisors.VSphere)]
        [InlineData(new[] { "hashicorp/vsphere" }, Hypervisors.VSphere)]
        [InlineData(new[] { "hashicorp/random" }, null)]
        [InlineData(new[] { "bpg/proxmox", "vmware/vsphere" }, null)]
        [InlineData(new string[0], null)]
        public void ForProviders_Names_The_One_Hypervisor(string[] providers, string expected)
        {
            Assert.Equal(expected, Hypervisors.ForProviders(providers));
        }

        [Fact]
        public void ForProviders_Of_Unread_Providers_Is_Null()
        {
            Assert.Null(Hypervisors.ForProviders(null));
        }

        private static TerraformOptions Options(Dictionary<string, string> direct = null, bool inheritAll = false, params string[] inherit) =>
            new()
            {
                EnvironmentVariables = new EnvironmentVariableOptions
                {
                    Direct = direct ?? [],
                    InheritAll = inheritAll,
                    Inherit = inherit
                }
            };

        [Fact]
        public void Configured_Reads_Direct_Endpoints()
        {
            var options = Options(new() { ["PROXMOX_VE_ENDPOINT"] = "https://pve:8006", ["VSPHERE_SERVER"] = " " });

            Assert.Equal([Hypervisors.Proxmox], Hypervisors.Configured(options, _ => null));
        }

        [Fact]
        public void Configured_Reads_Inherited_Endpoints_Only_When_Inherited()
        {
            string Env(string name) => name == "VSPHERE_SERVER" ? "vcenter.test" : name == "PROXMOX_VE_ENDPOINT" ? "https://pve:8006" : null;

            Assert.Empty(Hypervisors.Configured(Options(), Env));
            Assert.Equal([Hypervisors.VSphere], Hypervisors.Configured(Options(inherit: "VSPHERE_SERVER"), Env));
            Assert.Equal([Hypervisors.VSphere], Hypervisors.Configured(Options(inherit: "^VSPHERE"), Env));
            Assert.Equal(Hypervisors.All, Hypervisors.Configured(Options(inheritAll: true), Env));
        }

        [Fact]
        public void Configured_Direct_Value_Overrides_The_Inherited_One()
        {
            var options = Options(new() { ["VSPHERE_SERVER"] = "" }, inheritAll: true);

            Assert.DoesNotContain(Hypervisors.VSphere, Hypervisors.Configured(options, _ => "vcenter.test"));
        }

        [Fact]
        public void Configured_With_No_Options_Is_Empty()
        {
            Assert.Empty(Hypervisors.Configured(null, _ => "set"));
        }
    }
}
