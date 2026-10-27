// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using Caster.Api.Domain.Services.Inventory;
using Xunit;

namespace Caster.Api.Tests.Domain.Services.Inventory
{
    /// <summary>
    /// The snippets were checked with terraform validate against hashicorp/vsphere 2.12,
    /// bpg/proxmox 0.118 and Telmate/proxmox 3.0.2-rc10, and the Proxmox ones applied on
    /// the dev Proxmox. These pin their shape so they stay that way.
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

        private static readonly Dictionary<string, string> AlpineConfig = new()
        {
            ["agent"] = "enabled=1",
            ["bios"] = "ovmf",
            ["boot"] = "c",
            ["bootdisk"] = "scsi0",
            ["cicustom"] = "user=local:snippets/crucible-alpine-user-data.yaml",
            ["cipassword"] = "**********",
            ["ciuser"] = "root",
            ["cores"] = "1",
            ["digest"] = "0123456789abcdef",
            ["efidisk0"] = "local-lvm:base-105-disk-1,efitype=4m,pre-enrolled-keys=0,size=4M",
            ["ide2"] = "local-lvm:vm-105-cloudinit,media=cdrom",
            ["ipconfig0"] = "ip=dhcp",
            ["memory"] = "512",
            ["name"] = "alpine-linux-template",
            ["net0"] = "virtio=BC:24:11:44:67:F8,bridge=vmbr0",
            ["ostype"] = "l26",
            ["scsi0"] = "local-lvm:base-105-disk-0,discard=on,size=212M",
            ["serial0"] = "socket",
            ["template"] = "1",
            ["vga"] = "serial0"
        };

        [Fact]
        public void A_Proxmox_VM_Template_Clones_By_VMID_In_bpg()
        {
            Assert.Equal("""
                # In a proxmox_virtual_environment_vm resource:
                clone {
                  vm_id     = 105
                  node_name = "pve"
                }

                """, TerraformSnippets.ProxmoxTemplate("alpine-linux-template", 105, "pve", "qemu", AlpineConfig)[TerraformSnippets.Bpg]);
        }

        /// <summary>
        /// Applied on the dev Proxmox with Telmate/proxmox 3.0.2-rc10: the clone kept the
        /// template's disk, EFI disk, cloud-init drive, network, serial console and VGA.
        /// </summary>
        [Fact]
        public void A_Proxmox_VM_Template_Repeats_Its_Hardware_For_Telmate()
        {
            Assert.Equal("""
                # In a proxmox_vm_qemu resource (Telmate/proxmox 3.x). Telmate sets the clone
                # to exactly what the resource declares, so the template's hardware is repeated.
                clone       = "alpine-linux-template"
                target_node = "pve"
                memory      = 512
                balloon     = 512
                agent       = 1
                bios        = "ovmf"
                boot        = "c"
                bootdisk    = "scsi0"
                cicustom    = "user=local:snippets/crucible-alpine-user-data.yaml"
                ciuser      = "root"
                ipconfig0   = "ip=dhcp"
                qemu_os     = "l26"

                cpu {
                  cores = 1
                  type  = "kvm64"
                }

                disks {
                  ide {
                    ide2 {
                      cloudinit {
                        storage = "local-lvm"
                      }
                    }
                  }
                  scsi {
                    scsi0 {
                      disk {
                        storage = "local-lvm"
                        size    = "212M"
                        discard = true
                      }
                    }
                  }
                }

                efidisk {
                  efitype           = "4m"
                  storage           = "local-lvm"
                  pre_enrolled_keys = false
                }

                network {
                  id     = 0
                  model  = "virtio"
                  bridge = "vmbr0"
                }

                serial {
                  id   = 0
                  type = "socket"
                }

                vga {
                  type = "serial0"
                }

                # The template's cloud-init password cannot be read back, so set cipassword yourself.

                """, TerraformSnippets.ProxmoxTemplate("alpine-linux-template", 105, "pve", "qemu", AlpineConfig)[TerraformSnippets.Telmate]);
        }

        [Fact]
        public void A_Template_That_Boots_An_ISO_Keeps_Its_CDROM()
        {
            var snippet = TerraformSnippets.ProxmoxTemplate("tinycore-linux-template", 106, "pve", "qemu", new Dictionary<string, string>
            {
                ["boot"] = "order=ide2",
                ["cores"] = "1",
                ["ide2"] = "local:iso/TinyCore-current.iso,media=cdrom,size=24M",
                ["memory"] = "512",
                ["net0"] = "virtio=BC:24:11:37:39:D5,bridge=vmbr0",
                ["ostype"] = "l26"
            })[TerraformSnippets.Telmate];

            Assert.Contains("    ide2 {\n      cdrom {\n        iso = \"local:iso/TinyCore-current.iso\"\n      }\n    }\n", snippet);
            Assert.Contains("boot        = \"order=ide2\"\n", snippet);
            Assert.DoesNotContain("efidisk", snippet);
            Assert.DoesNotContain("cipassword", snippet);
        }

        [Fact]
        public void Telmate_Carries_Less_Common_Hardware_And_Names_What_It_Cannot()
        {
            var snippet = TerraformSnippets.ProxmoxTemplate("win2022", 300, "pve2", "qemu", new Dictionary<string, string>
            {
                ["cores"] = "4",
                ["sockets"] = "2",
                ["numa"] = "1",
                ["cpu"] = "cputype=x86-64-v2-AES,flags=+aes",
                ["memory"] = "8192",
                ["balloon"] = "2048",
                ["scsihw"] = "virtio-scsi-single",
                ["virtio0"] = "ceph:vm-300-disk-0,cache=writeback,iothread=1,backup=0,size=60G",
                ["scsi1"] = "local-lvm:vm-300-disk-1,iothread=1,ssd=1,size=8G",
                ["sata2"] = "local-lvm:vm-300-disk-3,iothread=1,ssd=1,size=1G",
                ["sata1"] = "none,media=cdrom",
                ["net1"] = "e1000=BC:24:11:00:00:01,bridge=vmbr1,tag=20,firewall=1",
                ["vga"] = "std,memory=64",
                ["hostpci0"] = "0000:01:00.0",
                ["unused0"] = "local-lvm:vm-300-disk-9"
            })[TerraformSnippets.Telmate];

            Assert.Contains("balloon     = 2048\n", snippet);
            Assert.Contains("scsihw      = \"virtio-scsi-single\"\n", snippet);
            Assert.Contains("cpu {\n  cores   = 4\n  type    = \"x86-64-v2-AES\"\n  sockets = 2\n  numa    = true\n}\n", snippet);
            Assert.Contains("    virtio0 {\n      disk {\n        storage  = \"ceph\"\n        size     = \"60G\"\n        iothread = true\n        cache    = \"writeback\"\n        backup   = false\n      }\n", snippet);
            // emulatessd is not a virtio argument, iothread not a sata one
            Assert.Contains("    scsi1 {\n      disk {\n        storage    = \"local-lvm\"\n        size       = \"8G\"\n        emulatessd = true\n        iothread   = true\n      }\n", snippet);
            Assert.Contains("    sata2 {\n      disk {\n        storage    = \"local-lvm\"\n        size       = \"1G\"\n        emulatessd = true\n      }\n", snippet);
            Assert.Contains("    sata1 {\n      cdrom {\n        passthrough = false\n      }\n    }\n", snippet);
            Assert.Contains("network {\n  id       = 1\n  model    = \"e1000\"\n  bridge   = \"vmbr1\"\n  tag      = 20\n  firewall = true\n}\n", snippet);
            Assert.Contains("vga {\n  type   = \"std\"\n  memory = 64\n}\n", snippet);
            Assert.Contains("# Not repeated here, so the clone will not have them: hostpci0, unused0\n", snippet);
            Assert.DoesNotContain("BC:24:11", snippet);
        }

        [Fact]
        public void Without_Its_Config_Or_As_A_Container_A_Template_Has_Only_A_bpg_Snippet()
        {
            Assert.Equal([TerraformSnippets.Bpg], TerraformSnippets.ProxmoxTemplate("alpine", 105, "pve", "qemu").Keys);
            Assert.Equal([TerraformSnippets.Bpg], TerraformSnippets.ProxmoxTemplate("alpine", 105, "pve", "qemu", new Dictionary<string, string>()).Keys);

            var container = TerraformSnippets.ProxmoxTemplate("debian-ct", 200, "pve", "lxc", AlpineConfig);
            Assert.Equal([TerraformSnippets.Bpg], container.Keys);
            Assert.StartsWith("# In a proxmox_virtual_environment_container resource:\n", container[TerraformSnippets.Bpg]);
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
