// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.Inventory;
using Caster.Api.Features.Designs;
using Caster.Api.Infrastructure.Options;

namespace Caster.Api.Tests.Features.Designs;

/// <summary>
/// <c>AiInventory</c> - which inventory a draft is grounded in, what the model is shown of it, and how the
/// names a draft uses are found in it. Built from a substituted cache; no provider is reached.
/// </summary>
[Trait("Category", "Unit")]
public class AiInventoryTests
{
    public static readonly DateTime Read = new(2026, 10, 29, 14, 5, 0, DateTimeKind.Utc);

    public static InventoryEntry Entry(string name, params (string Key, string Value)[] properties) => new()
    {
        Id = name,
        Name = name,
        Path = name,
        Properties = properties.ToDictionary(p => p.Key, p => p.Value)
    };

    public static InventorySnapshot Snapshot(InventoryEntry[] templates = null, InventoryEntry[] networks = null, InventoryEntry[] datastores = null) => new()
    {
        LastUpdated = Read,
        VmTemplates = InventoryCategoryResult.Ok(templates ?? []),
        Networks = InventoryCategoryResult.Ok(networks ?? []),
        Datastores = InventoryCategoryResult.Ok(datastores ?? []),
    };

    public static InventorySnapshot Proxmox() => Snapshot(
        [Entry("ubuntu-22", ("vmid", "9000"), ("node", "pve1"), ("maxcpu", "2"), ("maxmem", "4294967296"), ("maxdisk", "34359738368"))],
        [Entry("vmbr0", ("type", "bridge"), ("cidr", "192.168.1.0/24"), ("comment", "Uplink")), Entry("corp", ("type", "vnet"), ("zone", "lab"), ("tag", "10"))],
        [Entry("local-lvm", ("type", "lvmthin"), ("capacity", "2199023255552"), ("freeSpace", "107374182400"))]);

    public static IInventoryService Service(params (string Name, string Provider, InventorySnapshot Snapshot)[] connections)
    {
        var service = Substitute.For<IInventoryService>();
        service.GetConnections().Returns(connections
            .Select(c => new InventoryConnectionInfo { Name = c.Name, Provider = c.Provider, Enabled = true, IsDefault = c.Name == "default" })
            .ToList());
        foreach (var c in connections)
            service.GetSnapshot(c.Name).Returns(c.Snapshot);
        return service;
    }

    [Fact]
    public void Load_is_unchecked_while_the_inventory_is_not_enabled()
    {
        var service = Substitute.For<IInventoryService>();
        service.GetConnections().Returns([new InventoryConnectionInfo { Name = "default", Provider = "vsphere", Enabled = false }]);

        var inventory = AiInventory.Load(service, [Hypervisors.VSphere]);

        Assert.False(inventory.Grounding.Checked);
        Assert.Contains("not enabled", inventory.Grounding.Reason);
        Assert.Equal("", inventory.BuildPromptSection());
    }

    [Fact]
    public void Load_takes_only_the_connections_of_the_draft_hypervisor()
    {
        var service = Service(("default", InfrastructureProviders.Vsphere, Snapshot()), ("lab", InfrastructureProviders.Proxmox, Proxmox()));

        var inventory = AiInventory.Load(service, [Hypervisors.Proxmox]);

        Assert.True(inventory.Grounding.Checked);
        Assert.Equal(["lab"], inventory.Grounding.Connections.Select(c => c.Name));
        Assert.Equal(Hypervisors.Proxmox, inventory.Grounding.Connections[0].Hypervisor);
    }

    [Fact]
    public void Load_says_when_no_connection_reads_the_hypervisor()
    {
        var inventory = AiInventory.Load(Service(("default", InfrastructureProviders.Vsphere, Snapshot())), [Hypervisors.Proxmox]);

        Assert.False(inventory.Grounding.Checked);
        Assert.Contains("Proxmox", inventory.Grounding.Reason);
    }

    [Fact]
    public void Load_is_unchecked_when_nothing_could_be_read()
    {
        var inventory = AiInventory.Load(Service(("default", InfrastructureProviders.Proxmox, InventorySnapshot.AllUnavailable("connection refused"))), null);

        Assert.False(inventory.Grounding.Checked);
        Assert.Contains("connection refused", inventory.Grounding.Reason);
    }

    [Fact]
    public void Load_names_a_category_it_could_not_read()
    {
        var snapshot = Proxmox();
        snapshot.Datastores = InventoryCategoryResult.Unavailable("permission denied");

        var inventory = AiInventory.Load(Service(("default", InfrastructureProviders.Proxmox, snapshot)), null);

        Assert.True(inventory.Grounding.Checked);
        Assert.Equal([AiInventoryKind.Datastore], inventory.Grounding.Connections[0].Unavailable);
        Assert.Contains("permission denied", inventory.Grounding.Reason);
        Assert.Contains("Datastores could not be read", inventory.BuildPromptSection());
    }

    [Fact]
    public void The_prompt_lists_templates_networks_and_datastores_with_their_details()
    {
        var prompt = AiInventory.Load(Service(("default", InfrastructureProviders.Proxmox, Proxmox())), null).BuildPromptSection();

        Assert.Contains("## Proxmox connection \"default\" (read 2026-10-29 14:05 UTC)", prompt);
        Assert.Contains("- ubuntu-22 (vmid 9000, node pve1, 2 cpu, 4 GiB, 32 GiB disk)", prompt);
        Assert.Contains("- corp (vnet, zone lab, VLAN 10)", prompt);
        Assert.Contains("- vmbr0 (bridge, 192.168.1.0/24, \"Uplink\")", prompt);
        Assert.Contains("- local-lvm (lvmthin, 100 GiB free of 2 TiB)", prompt);
    }

    [Fact]
    public void The_prompt_marks_a_stale_category()
    {
        var snapshot = Proxmox();
        snapshot.Networks.Stale = true;

        var inventory = AiInventory.Load(Service(("default", InfrastructureProviders.Proxmox, snapshot)), null);

        Assert.Contains("Networks (from an earlier read, may be out of date):", inventory.BuildPromptSection());
        Assert.True(inventory.Grounding.Connections[0].Stale);
    }

    [Fact]
    public void The_prompt_counts_what_it_leaves_out()
    {
        var networks = Enumerable.Range(0, AiInventory.MaxNetworks + 5).Select(i => Entry($"vnet{i:000}")).ToArray();
        var inventory = AiInventory.Load(Service(("default", InfrastructureProviders.Proxmox, Snapshot(networks: networks))), null);

        var prompt = inventory.BuildPromptSection();

        Assert.Contains("- and 5 more not listed", prompt);
        Assert.True(inventory.Grounding.Connections[0].Truncated);
        Assert.True(prompt.Length <= AiInventory.MaxPromptCharacters);
    }

    [Fact]
    public void The_prompt_keeps_within_its_budget_for_a_large_inventory()
    {
        var templates = Enumerable.Range(0, 2000).Select(i => Entry(new string('t', 150) + i)).ToArray();
        var inventory = AiInventory.Load(Service(("default", InfrastructureProviders.Proxmox, Snapshot(templates: templates))), null);

        Assert.True(inventory.BuildPromptSection().Length <= AiInventory.MaxPromptCharacters);
    }

    [Fact]
    public void Check_records_found_and_unknown_names_and_warns_about_the_unknown()
    {
        var inventory = AiInventory.Load(Service(("default", InfrastructureProviders.Proxmox, Proxmox())), [Hypervisors.Proxmox]);
        var warnings = new List<string>();

        inventory.Check(
        [
            new AiNameUse(AiInventoryKind.Network, "vmbr0", "main.tf", Hypervisors.Proxmox),
            new AiNameUse(AiInventoryKind.Network, "dmz", "main.tf", Hypervisors.Proxmox),
            new AiNameUse(AiInventoryKind.Template, "9000", "main.tf", Hypervisors.Proxmox, ByVmId: true),
            new AiNameUse(AiInventoryKind.Template, "9001", "main.tf", Hypervisors.Proxmox, ByVmId: true),
        ], warnings);

        Assert.Equal(["vmbr0", "9000"], inventory.Grounding.Found.Select(f => f.Name));
        Assert.All(inventory.Grounding.Found, f => Assert.Equal("default", f.Connection));
        Assert.Equal(["dmz", "9001"], inventory.Grounding.Unknown.Select(f => f.Name));
        Assert.Contains("main.tf: network 'dmz' is not in the Proxmox inventory. Use one that is listed, create it, or make it a variable.", warnings);
        Assert.Contains(warnings, w => w.Contains("template vmid 9001"));
    }

    [Fact]
    public void Check_names_the_listed_name_that_differs_only_in_case()
    {
        var inventory = AiInventory.Load(Service(("default", InfrastructureProviders.Proxmox, Proxmox())), null);
        var warnings = new List<string>();

        inventory.Check([new AiNameUse(AiInventoryKind.Network, "VMBR0", "main.tf")], warnings);

        Assert.Contains(warnings, w => w.Contains("which has 'vmbr0'"));
    }

    [Fact]
    public void Check_finds_a_vSphere_template_by_the_end_of_its_folder_path()
    {
        var template = Entry("win2022");
        template.Path = "Templates/Windows/win2022";
        var inventory = AiInventory.Load(Service(("default", InfrastructureProviders.Vsphere, Snapshot(templates: [template]))), null);
        var warnings = new List<string>();

        inventory.Check([new AiNameUse(AiInventoryKind.Template, "Windows/win2022", "main.tf", Hypervisors.VSphere)], warnings);

        Assert.Empty(warnings);
        Assert.Single(inventory.Grounding.Found);
    }

    [Fact]
    public void Check_skips_a_kind_whose_category_could_not_be_read()
    {
        var snapshot = Proxmox();
        snapshot.VmTemplates = InventoryCategoryResult.Unavailable("denied");
        var inventory = AiInventory.Load(Service(("default", InfrastructureProviders.Proxmox, snapshot)), null);
        var warnings = new List<string>();

        inventory.Check([new AiNameUse(AiInventoryKind.Template, "anything", "main.tf")], warnings);

        Assert.Empty(warnings);
        Assert.Empty(inventory.Grounding.Unknown);
    }

    [Fact]
    public void Check_lists_a_name_used_twice_once_with_both_places()
    {
        var inventory = AiInventory.Load(Service(("default", InfrastructureProviders.Proxmox, Proxmox())), null);
        var warnings = new List<string>();

        inventory.Check([new AiNameUse(AiInventoryKind.Network, "dmz", "a.tf"), new AiNameUse(AiInventoryKind.Network, "dmz", "b.tf")], warnings);

        Assert.Equal("a.tf, b.tf", Assert.Single(inventory.Grounding.Unknown).Where);
        Assert.Single(warnings);
    }

    [Fact]
    public void TemplateSize_and_DatastoreSpace_read_the_inventory()
    {
        var inventory = AiInventory.Load(Service(("default", InfrastructureProviders.Proxmox, Proxmox())), null);

        Assert.Equal((2, 4096, 32), inventory.TemplateSize(Hypervisors.Proxmox, "ubuntu-22"));
        Assert.Equal((2, 4096, 32), inventory.TemplateSize(Hypervisors.Proxmox, "9000"));
        Assert.Equal(default, inventory.TemplateSize(Hypervisors.VSphere, "ubuntu-22"));
        Assert.Equal((107374182400L, 2199023255552L), inventory.DatastoreSpace(Hypervisors.Proxmox));
    }
}
