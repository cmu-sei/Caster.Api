// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.IO;
using Caster.Api.Domain.Services.Modules;
using Xunit;

namespace Caster.Api.Tests.Domain.Services.Modules
{
    [Trait("Category", "Unit")]
    [Trait("Category", "Modules")]
    public class TerraformProviderReaderTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), $"caster-providers-{Guid.NewGuid():N}");

        public TerraformProviderReaderTests() => Directory.CreateDirectory(_dir);

        public void Dispose() => Directory.Delete(_dir, true);

        private void Write(string name, string text) => File.WriteAllText(Path.Combine(_dir, name), text);

        [Fact]
        public void Reads_Hcl_Required_Providers()
        {
            Write("main.tf", """
                terraform {
                  required_version = ">= 1.5"
                  required_providers {
                    proxmox = {
                      source  = "bpg/proxmox"
                      version = ">= 0.66"
                    }
                    random = { source = "hashicorp/random" }
                  }
                }

                resource "proxmox_virtual_environment_vm" "vm" {
                  source = "not/a-provider"
                }
                """);

            Assert.Equal(["bpg/proxmox", "hashicorp/random"], TerraformProviderReader.Read(_dir));
        }

        [Fact]
        public void Ignores_Commented_Out_Providers_But_Not_Urls_In_Strings()
        {
            Write("main.tf", """
                terraform {
                  required_providers {
                    # old = { source = "hashicorp/vsphere" }
                    /* vsphere = { source = "vmware/vsphere" } */
                    proxmox = {
                      source = "BPG/Proxmox" // trailing comment
                    }
                  }
                }
                locals { url = "https://example.test/{" }
                """);

            Assert.Equal(["bpg/proxmox"], TerraformProviderReader.Read(_dir));
        }

        [Fact]
        public void Reads_Json_Required_Providers_As_Object_Or_List()
        {
            Write("versions.tf.json", """
                { "terraform": [ { "required_providers": [ { "vsphere": { "source": "vmware/vsphere" } } ] } ] }
                """);
            Write("other.tf.json", """
                { "terraform": { "required_providers": { "null": { "source": "hashicorp/null" } } } }
                """);

            Assert.Equal(["hashicorp/null", "vmware/vsphere"], TerraformProviderReader.Read(_dir));
        }

        [Fact]
        public void Skips_Json_That_Does_Not_Parse_And_Nested_Directories()
        {
            Write("broken.tf.json", "{ not json");
            Directory.CreateDirectory(Path.Combine(_dir, "nested"));
            File.WriteAllText(Path.Combine(_dir, "nested", "main.tf"),
                "terraform { required_providers { p = { source = \"bpg/proxmox\" } } }");

            Assert.Empty(TerraformProviderReader.Read(_dir));
        }

        [Fact]
        public void Missing_Directory_Declares_Nothing()
        {
            Assert.Empty(TerraformProviderReader.Read(Path.Combine(_dir, "missing")));
        }
    }
}
