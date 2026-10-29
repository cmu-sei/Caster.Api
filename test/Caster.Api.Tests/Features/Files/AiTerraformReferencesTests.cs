// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Designs;
using Caster.Api.Features.Files;

namespace Caster.Api.Tests.Features.Files;

/// <summary>
/// <c>AiTerraformReferences</c> - what a draft's HCL says about the range: its hypervisors, the template,
/// network and datastore names it sets as literals, and the networks it creates.
/// </summary>
[Trait("Category", "Unit")]
public class AiTerraformReferencesTests
{
    private const string Bpg = """
        resource "proxmox_virtual_environment_vm" "dc" {
          node_name = var.node
          clone {
            vm_id = 9000
          }
          disk {
            datastore_id = "local-lvm"
          }
          network_device {
            bridge = "corp"
          }
          network_device {
            bridge = "${var.uplink}"
          }
          # network_device { bridge = "commented" }
        }
        """;

    [Fact]
    public void NameUses_reads_a_bpg_machine()
    {
        var uses = AiTerraformReferences.NameUses("dc.tf", Bpg).ToList();

        Assert.Equal(3, uses.Count);
        Assert.Contains(new AiNameUse(AiInventoryKind.Template, "9000", "dc.tf proxmox_virtual_environment_vm.dc", Hypervisors.Proxmox, ByVmId: true), uses);
        Assert.Contains(new AiNameUse(AiInventoryKind.Network, "corp", "dc.tf proxmox_virtual_environment_vm.dc", Hypervisors.Proxmox), uses);
        Assert.Contains(new AiNameUse(AiInventoryKind.Datastore, "local-lvm", "dc.tf proxmox_virtual_environment_vm.dc", Hypervisors.Proxmox), uses);
    }

    [Fact]
    public void NameUses_reads_a_Telmate_machine()
    {
        var uses = AiTerraformReferences.NameUses("vm.tf", """
            resource "proxmox_vm_qemu" "ws" {
              clone = "win11-template"
              network {
                bridge = "vmbr1"
              }
            }
            """).ToList();

        Assert.Contains(uses, u => u.Kind == AiInventoryKind.Template && u.Name == "win11-template" && !u.ByVmId);
        Assert.Contains(uses, u => u.Kind == AiInventoryKind.Network && u.Name == "vmbr1");
    }

    [Fact]
    public void NameUses_reads_vSphere_data_sources()
    {
        var uses = AiTerraformReferences.NameUses("data.tf", """
            data "vsphere_virtual_machine" "template" {
              name          = "Templates/win2022"
              datacenter_id = data.vsphere_datacenter.dc.id
            }
            data "vsphere_network" "corp" {
              name = "corp-pg"
            }
            data "vsphere_datastore" "ds" {
              name = var.datastore
            }
            """).ToList();

        Assert.Equal(2, uses.Count);
        Assert.All(uses, u => Assert.Equal(Hypervisors.VSphere, u.Hypervisor));
        Assert.Contains(uses, u => u.Kind == AiInventoryKind.Template && u.Name == "Templates/win2022");
        Assert.Contains(uses, u => u.Kind == AiInventoryKind.Network && u.Name == "corp-pg");
    }

    [Fact]
    public void NameUses_reads_the_defaults_of_variables_named_for_a_template_network_or_datastore()
    {
        var uses = AiTerraformReferences.NameUses("variables.tf", """
            variable "kali_template" {
              default = "kali-2026"
            }
            variable "dmz_network" {
              type    = string
              default = "dmz"
            }
            variable "network_cidr" {
              default = "10.0.0.0/24"
            }
            variable "vm_count" {
              default = "2"
            }
            """).ToList();

        Assert.Equal(["kali-2026", "dmz"], uses.Select(u => u.Name));
        Assert.All(uses, u => Assert.Null(u.Hypervisor));
    }

    [Fact]
    public void NameUses_reads_tfvars_assignments()
    {
        var uses = AiTerraformReferences.NameUses("lab.tfvars", "template_id = \"9001\"\nvm_name = \"web\"\nstorage = \"ceph\"\n").ToList();

        Assert.Contains(uses, u => u.Kind == AiInventoryKind.Template && u.Name == "9001" && u.ByVmId);
        Assert.Contains(uses, u => u.Kind == AiInventoryKind.Datastore && u.Name == "ceph");
        Assert.Equal(2, uses.Count);
    }

    [Fact]
    public void CreatedNetworks_are_the_bridges_vnets_and_port_groups_the_files_make()
    {
        var created = AiTerraformReferences.CreatedNetworks("""
            resource "proxmox_virtual_environment_network_linux_bridge" "lan" {
              node_name = "pve1"
              name      = "vmbr20"
            }
            resource "proxmox_virtual_environment_sdn_vnet" "ot" {
              id   = "ot"
              zone = "lab"
            }
            resource "vsphere_distributed_port_group" "dmz" {
              name = "dmz-pg"
            }
            """).ToList();

        Assert.Equal(["vmbr20", "ot", "dmz-pg"], created);
    }

    [Theory]
    [InlineData("terraform {\n  required_providers {\n    proxmox = { source = \"bpg/proxmox\" }\n  }\n}", new[] { "Proxmox" })]
    [InlineData("provider \"vsphere\" {}\ndata \"vsphere_datacenter\" \"dc\" {}", new[] { "vSphere" })]
    [InlineData("resource \"proxmox_vm_qemu\" \"a\" {}\nresource \"vsphere_folder\" \"f\" {}", new[] { "Proxmox", "vSphere" })]
    [InlineData("resource \"random_password\" \"p\" {}", new string[0])]
    public void DetectHypervisors_reads_providers_and_resource_types(string content, string[] expected)
    {
        Assert.Equal(expected, AiTerraformReferences.DetectHypervisors([content]));
    }

    [Theory]
    [InlineData("windows_template", AiInventoryKind.Template)]
    [InlineData("lan_bridge", AiInventoryKind.Network)]
    [InlineData("vnet_name", AiInventoryKind.Network)]
    [InlineData("datastore_id", AiInventoryKind.Datastore)]
    [InlineData("network_gateway", null)]
    [InlineData("hostname", null)]
    public void KindOfVariable_reads_the_name(string name, AiInventoryKind? expected)
    {
        Assert.Equal(expected, AiTerraformReferences.KindOfVariable(name));
    }
}
