// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using Caster.Api.Domain.Services.Inventory;
using Caster.Api.Infrastructure.Options;
using Xunit;

namespace Caster.Api.Tests.Domain.Services.Inventory
{
    /// <summary>
    /// A connection's Inventory:Resources settings: which kinds of object are read, and
    /// the Locations, Include and Exclude filters on each.
    /// </summary>
    [Trait("Category", "Unit")]
    [Trait("Category", "Inventory")]
    public class InventoryFiltersTests
    {
        private static InfrastructureConnectionOptions Vsphere() => new() { Name = "lab", Provider = "vsphere", Inventory = { Enabled = true } };

        private static InfrastructureConnectionOptions Proxmox() => new() { Name = "pve", Provider = "proxmox", Inventory = { Enabled = true } };

        private static InventoryEntry Entry(string name, params string[] locations) =>
            new() { Id = name, Name = name, Path = locations.FirstOrDefault() ?? name, Locations = locations };

        private static InventorySnapshot Templates(params InventoryEntry[] entries) => new()
        {
            VmTemplates = InventoryCategoryResult.Ok(entries),
            Isos = InventoryCategoryResult.Ok([]),
            Networks = InventoryCategoryResult.Ok([]),
            Datastores = InventoryCategoryResult.Ok([]),
            ContentLibrary = InventoryCategoryResult.Ok([]),
            Hosts = InventoryCategoryResult.Ok([])
        };

        [Fact]
        public void Every_kind_of_object_is_on_by_default()
        {
            Assert.Equal(InventoryFilters.Categories, InventoryFilters.Enabled(new InfrastructureConnectionOptions()));
            Assert.Null(InventoryFilters.Validate(Vsphere()));
            Assert.Null(InventoryFilters.Validate(Proxmox()));
        }

        [Fact]
        public void A_kind_that_is_turned_off_is_reported_as_not_enabled_rather_than_unavailable()
        {
            var options = Vsphere();
            options.Inventory.Resources.VmTemplates.Enabled = false;

            var snapshot = InventoryFilters.Apply(Templates(Entry("ubuntu")), options);

            Assert.False(snapshot.VmTemplates.Enabled);
            Assert.False(snapshot.VmTemplates.Available);
            Assert.Empty(snapshot.VmTemplates.Entries);
            Assert.Contains("Infrastructure:Inventory:Resources:VmTemplates:Enabled", snapshot.VmTemplates.Error);
            Assert.True(snapshot.Networks.Enabled);
            Assert.DoesNotContain(InventoryCategory.VmTemplate, InventoryFilters.Enabled(options));
        }

        [Fact]
        public void A_kind_the_provider_does_not_have_stays_not_applicable_when_turned_off()
        {
            var options = Proxmox();
            options.Inventory.Resources.ContentLibraries.Enabled = false;
            var snapshot = Templates();
            snapshot.ContentLibrary = InventoryCategoryResult.NotApplicable(InventoryMessages.ContentLibraryNotOnProxmox);

            snapshot = InventoryFilters.Apply(snapshot, options);

            Assert.False(snapshot.ContentLibrary.Applies);
            Assert.Equal(InventoryMessages.ContentLibraryNotOnProxmox, snapshot.ContentLibrary.Error);
        }

        [Fact]
        public void Include_and_Exclude_ignore_case_and_Exclude_wins()
        {
            var options = Vsphere();
            options.Inventory.Resources.VmTemplates.Include = "^LAB-";
            options.Inventory.Resources.VmTemplates.Exclude = "-old$";

            var snapshot = InventoryFilters.Apply(Templates(Entry("lab-ubuntu"), Entry("lab-ubuntu-OLD"), Entry("prod-ubuntu")), options);

            Assert.Equal(["lab-ubuntu"], snapshot.VmTemplates.Entries.Select(x => x.Name));
        }

        [Fact]
        public void A_path_location_matches_that_folder_and_below_ignoring_case()
        {
            var options = Vsphere();
            options.Inventory.Resources.VmTemplates.Locations = ["/dc1/vm/templates/"];

            var snapshot = InventoryFilters.Apply(Templates(
                Entry("a", "/DC1/vm/Templates/linux/a"),
                Entry("b", "/DC1/vm/Templates"),
                Entry("c", "/DC1/vm/TemplatesOld/c"),
                Entry("d", "/DC2/vm/Templates/d"),
                Entry("e")), options);

            Assert.Equal(["a", "b"], snapshot.VmTemplates.Entries.Select(x => x.Name));
        }

        [Fact]
        public void A_name_location_matches_a_name_and_not_a_path()
        {
            var options = Vsphere();
            options.Inventory.Resources.Networks.Locations = ["dswitch-lab"];
            var snapshot = Templates();
            snapshot.Networks = InventoryCategoryResult.Ok([
                Entry("pg-1", "/DC1/network/pg-1", "DSwitch-Lab"),
                Entry("pg-2", "/DC1/network/dswitch-lab/pg-2", "DSwitch-Prod"),
                Entry("VM Network", "/DC1/network/VM Network")
            ]);

            snapshot = InventoryFilters.Apply(snapshot, options);

            Assert.Equal(["pg-1"], snapshot.Networks.Entries.Select(x => x.Name));
        }

        [Fact]
        public void A_failed_read_is_left_as_it_is()
        {
            var options = Vsphere();
            options.Inventory.Resources.VmTemplates.Include = "^lab-";
            var snapshot = Templates();
            snapshot.VmTemplates = InventoryCategoryResult.Unavailable("vCenter returned 503.");

            snapshot = InventoryFilters.Apply(snapshot, options);

            Assert.True(snapshot.VmTemplates.Enabled);
            Assert.Equal("vCenter returned 503.", snapshot.VmTemplates.Error);
        }

        [Theory]
        [InlineData("Include")]
        [InlineData("Exclude")]
        public void An_invalid_regular_expression_is_a_misconfiguration(string which)
        {
            var options = Vsphere();
            if (which == "Include")
                options.Inventory.Resources.Networks.Include = "lab-(";
            else
                options.Inventory.Resources.Networks.Exclude = "lab-(";

            var problem = InventoryFilters.Validate(options);

            Assert.Contains($"Infrastructure:Inventory:Resources:Networks:{which} is not a valid regular expression", problem);
        }

        [Fact]
        public void A_vsphere_location_must_be_a_path_except_a_networks_switch_name()
        {
            var options = Vsphere();
            options.Inventory.Resources.VmTemplates.Locations = ["Templates"];
            options.Inventory.Resources.Datastores.Locations = ["/DC1/datastore"];
            options.Inventory.Resources.Networks.Locations = ["DSwitch", "/DC1/network/Lab"];

            var problem = InventoryFilters.Validate(options);

            Assert.Contains("VmTemplates:Locations names \"Templates\"", problem);
            Assert.DoesNotContain("Datastores", problem);
            Assert.DoesNotContain("Networks", problem);
        }

        [Fact]
        public void A_proxmox_location_is_a_name()
        {
            var options = Proxmox();
            options.Inventory.Resources.Hosts.Locations = ["pve1", "/pve2"];

            Assert.Contains("Hosts:Locations names \"/pve2\"", InventoryFilters.Validate(options));
        }

        [Theory]
        [InlineData("Isos")]
        [InlineData("ContentLibraries")]
        public void Locations_does_not_apply_to_isos_or_content_libraries(string key)
        {
            var options = Vsphere();
            var settings = key == "Isos" ? options.Inventory.Resources.Isos : options.Inventory.Resources.ContentLibraries;
            settings.Locations = ["/DC1"];

            Assert.Contains($"Infrastructure:Inventory:Resources:{key}:Locations doesn't apply", InventoryFilters.Validate(options));
        }

        [Fact]
        public void Datacenters_is_only_for_vsphere()
        {
            var options = Proxmox();
            options.Datacenters = ["DC1"];

            Assert.Contains("Infrastructure:Datacenters is only for vsphere connections.", InventoryFilters.Validate(options));
            Assert.Null(InventoryFilters.Validate(new InfrastructureConnectionOptions { Provider = "vsphere", Datacenters = ["DC1"] }));
        }
    }
}
