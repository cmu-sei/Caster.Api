// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Caster.Api.Domain.Services.Inventory;
using Xunit;

namespace Caster.Api.Tests.Domain.Services.Inventory
{
    /// <summary>
    /// The snippets were checked with terraform validate against hashicorp/vsphere 2.12,
    /// bpg/proxmox 0.118 and Telmate/proxmox 2.9. These pin their shape so they stay that way.
    /// </summary>
    [Trait("Category", "Unit")]
    [Trait("Category", "Inventory")]
    public class TerraformSnippetsTests
    {
        [Fact]
        public void A_Vsphere_Template_Is_Found_By_Its_Path_In_Its_Datacenter()
        {
            var snippet = TerraformSnippets.VsphereTemplate("alpine-template", "Templates/Linux/alpine-template", "DC1")[TerraformSnippets.Vsphere];

            Assert.Equal("""
                data "vsphere_datacenter" "alpine_template_datacenter" {
                  name = "DC1"
                }

                data "vsphere_virtual_machine" "alpine_template" {
                  name          = "Templates/Linux/alpine-template"
                  datacenter_id = data.vsphere_datacenter.alpine_template_datacenter.id
                }

                # In a vsphere_virtual_machine resource:
                #   clone {
                #     template_uuid = data.vsphere_virtual_machine.alpine_template.id
                #   }

                """, snippet);
        }

        [Fact]
        public void A_Vsphere_Template_Without_A_Datacenter_Leaves_The_Datacenter_Out()
        {
            var snippet = TerraformSnippets.VsphereTemplate("win2022", "win2022", null)[TerraformSnippets.Vsphere];

            Assert.DoesNotContain("vsphere_datacenter", snippet);
            Assert.DoesNotContain("datacenter_id", snippet);
            Assert.Contains("data \"vsphere_virtual_machine\" \"win2022\" {\n  name = \"win2022\"\n}", snippet);
        }

        [Fact]
        public void A_Content_Library_Item_Is_Found_By_Name_And_Type_In_Its_Library()
        {
            var snippet = TerraformSnippets.VsphereLibraryItem("ubuntu-2404", "ovf", "Golden")[TerraformSnippets.Vsphere];

            Assert.Contains("data \"vsphere_content_library\" \"ubuntu_2404_library\" {\n  name = \"Golden\"\n}", snippet);
            Assert.Contains("  type       = \"ovf\"\n", snippet);
            Assert.Contains("  library_id = data.vsphere_content_library.ubuntu_2404_library.id\n", snippet);
            Assert.Contains("template_uuid = data.vsphere_content_library_item.ubuntu_2404.id", snippet);
        }

        [Fact]
        public void A_Proxmox_VM_Template_Clones_By_VMID_In_bpg_And_By_Name_In_Telmate()
        {
            var snippets = TerraformSnippets.ProxmoxTemplate("alpine-template", 105, "pve", "qemu");

            Assert.Equal("""
                # In a proxmox_virtual_environment_vm resource:
                clone {
                  vm_id     = 105
                  node_name = "pve"
                }

                """, snippets[TerraformSnippets.Bpg]);
            Assert.Equal("""
                # In a proxmox_vm_qemu resource:
                clone       = "alpine-template"
                target_node = "pve"

                """, snippets[TerraformSnippets.Telmate]);
        }

        [Fact]
        public void A_Proxmox_Container_Template_Clones_By_VMID_In_Both()
        {
            var snippets = TerraformSnippets.ProxmoxTemplate("debian-ct", 200, "pve", "lxc");

            Assert.StartsWith("# In a proxmox_virtual_environment_container resource:\n", snippets[TerraformSnippets.Bpg]);
            Assert.Contains("clone       = \"200\"", snippets[TerraformSnippets.Telmate]);
            Assert.StartsWith("# In a proxmox_lxc resource:\n", snippets[TerraformSnippets.Telmate]);
        }

        [Fact]
        public void A_Proxmox_Template_Without_A_VMID_Or_Node_Has_No_Snippet()
        {
            Assert.Empty(TerraformSnippets.ProxmoxTemplate("x", null, "pve", "qemu"));
            Assert.Empty(TerraformSnippets.ProxmoxTemplate("x", 105, " ", "qemu"));
        }

        [Theory]
        [InlineData("alpine-template", "alpine_template")]
        [InlineData("Win 2019 (Core)", "win_2019_core")]
        [InlineData("2019-server", "template_2019_server")]
        [InlineData("---", "template")]
        [InlineData(null, "template")]
        public void Labels_Are_Valid_Block_Labels(string name, string label)
        {
            Assert.Equal(label, TerraformSnippets.Label(name));
        }

        [Theory]
        [InlineData("plain", "\"plain\"")]
        [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
        [InlineData(@"C:\tmp", "\"C:\\\\tmp\"")]
        [InlineData("${var.x}", "\"$${var.x}\"")]
        [InlineData("%{ if }", "\"%%{ if }\"")]
        public void Values_Are_Quoted_So_They_Stay_Literal(string value, string quoted)
        {
            Assert.Equal(quoted, TerraformSnippets.Quote(value));
        }
    }
}
