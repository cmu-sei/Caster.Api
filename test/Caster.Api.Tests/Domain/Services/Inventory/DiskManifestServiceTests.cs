// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.Inventory;
using NSubstitute;
using Xunit;

namespace Caster.Api.Tests.Domain.Services.Inventory
{
    [Trait("Category", "Unit")]
    [Trait("Category", "Inventory")]
    public class DiskManifestServiceTests
    {
        private const string Iso = "local:iso/kali-2026.3.iso";

        private static InventoryEntry Entry(string name, string id = null, string storage = null)
        {
            var entry = new InventoryEntry { Id = id ?? name, Name = name, Path = name };

            if (storage != null)
                entry.Properties["storage"] = storage;

            return entry;
        }

        /// <summary>
        /// Without <paramref name="library"/> the connection has no content libraries at all,
        /// as on Proxmox.
        /// </summary>
        private static InventorySnapshot Snapshot(InventoryEntry[] templates, InventoryEntry[] isos = null, InventoryEntry[] library = null) => new()
        {
            VmTemplates = InventoryCategoryResult.Ok(templates),
            Isos = isos == null
                ? InventoryCategoryResult.Unavailable(InventoryMessages.IsoNotSupported)
                : InventoryCategoryResult.Ok(isos),
            Networks = InventoryCategoryResult.Ok([]),
            Datastores = InventoryCategoryResult.Ok([]),
            ContentLibrary = library == null
                ? InventoryCategoryResult.NotApplicable(InventoryMessages.ContentLibraryNotOnProxmox)
                : InventoryCategoryResult.Ok(library)
        };

        private static InventoryEntry LibraryItem(string library, string name)
        {
            var entry = new InventoryEntry { Id = $"item-{name}", Name = name, Path = $"{library}/{name}" };
            entry.Properties["library"] = library;
            entry.Properties["type"] = "vm-template";
            return entry;
        }

        private static IInventoryService Inventory(params (string Name, string Provider, InventorySnapshot Snapshot)[] connections)
        {
            var inventory = Substitute.For<IInventoryService>();

            inventory.GetConnections().Returns(connections
                .Select(x => new InventoryConnectionInfo { Name = x.Name, Provider = x.Provider, Enabled = true })
                .ToList());

            foreach (var connection in connections)
            {
                inventory.GetSnapshot(connection.Name).Returns(connection.Snapshot);
            }

            return inventory;
        }

        private static IInventoryService Disabled()
        {
            var inventory = Substitute.For<IInventoryService>();
            inventory.GetConnections().Returns([new InventoryConnectionInfo { Name = "default", Enabled = false }]);
            return inventory;
        }

        private static ArchiveFileEntry File(string path, string content) => new(path, content);

        #region Build

        [Fact]
        public void A_Quoted_Template_Name_Is_Listed_With_The_Files_That_Use_It()
        {
            var service = new DiskManifestService(Inventory(
                ("default", "proxmox", Snapshot([Entry("ubuntu-2404"), Entry("win2022")], [Entry(Iso, storage: "local")]))));

            var manifest = service.Build(
            [
                File("Range/main.tf", "module \"web\" {\n  template_name = \"ubuntu-2404\"\n}\n"),
                File("Range/__Workspaces__/blue/blue.auto.tfvars", "iso_path = \"local:iso/kali-2026.3.iso\"\ntemplate = \"ubuntu-2404\"\n")
            ]);

            Assert.Empty(manifest.Warnings);
            Assert.Collection(manifest.Disks,
                template =>
                {
                    Assert.Equal(DiskKind.VmTemplate, template.Kind);
                    Assert.Equal("ubuntu-2404", template.Name);
                    Assert.Equal("default", template.Connection);
                    Assert.Equal("proxmox", template.Provider);
                    Assert.Equal(["Range/main.tf", "Range/__Workspaces__/blue/blue.auto.tfvars"], template.ReferencedBy);
                },
                iso =>
                {
                    Assert.Equal(DiskKind.Iso, iso.Kind);
                    Assert.Equal(Iso, iso.Name);
                    Assert.Equal("local", iso.Properties["storage"]);
                    Assert.Equal(["Range/__Workspaces__/blue/blue.auto.tfvars"], iso.ReferencedBy);
                });
        }

        [Fact]
        public void A_Template_Nothing_Refers_To_Is_Not_Listed()
        {
            var service = new DiskManifestService(Inventory(("default", "vsphere", Snapshot([Entry("win2022")]))));

            var manifest = service.Build([File("main.tf", "template_name = \"ubuntu-2404\"\n")]);

            Assert.Empty(manifest.Disks);
        }

        /// <summary>
        /// The value of an interpolated string is only known at plan time, so a template name
        /// that happens to appear inside one is not a reference to it.
        /// </summary>
        [Fact]
        public void An_Interpolated_String_Is_Not_A_Reference()
        {
            var service = new DiskManifestService(Inventory(("default", "vsphere", Snapshot([Entry("ubuntu-2404")]))));

            var manifest = service.Build([File("main.tf", "name = \"${var.prefix}ubuntu-2404\"\nalso = \"ubuntu-2404-${var.n}\"\n")]);

            Assert.Empty(manifest.Disks);
        }

        [Fact]
        public void A_Name_Is_Matched_Whole_And_Case_Sensitively()
        {
            var service = new DiskManifestService(Inventory(("default", "vsphere", Snapshot([Entry("ubuntu")]))));

            var manifest = service.Build([File("main.tf", "a = \"ubuntu-2404\"\nb = \"Ubuntu\"\n# ubuntu\n")]);

            Assert.Empty(manifest.Disks);
        }

        [Fact]
        public void An_Escaped_Quote_Does_Not_End_The_String()
        {
            var service = new DiskManifestService(Inventory(("default", "vsphere", Snapshot([Entry("say \"hi\"")]))));

            var manifest = service.Build([File("main.tf", "name = \"say \\\"hi\\\"\"\n")]);

            Assert.Equal("say \"hi\"", Assert.Single(manifest.Disks).Name);
        }

        [Fact]
        public void A_Template_On_Two_Connections_Is_Listed_Once_For_Each()
        {
            var service = new DiskManifestService(Inventory(
                ("default", "vsphere", Snapshot([Entry("ubuntu-2404", id: "vm-1")])),
                ("lab", "proxmox", Snapshot([Entry("ubuntu-2404", id: "qemu/105")]))));

            var manifest = service.Build([File("main.tf", "template_name = \"ubuntu-2404\"\n")]);

            Assert.Equal(["default", "lab"], manifest.Disks.Select(x => x.Connection));
        }

        [Fact]
        public void An_Unreadable_Category_Is_Reported_Rather_Than_Silently_Empty()
        {
            var service = new DiskManifestService(Inventory(("default", "vsphere", Snapshot([Entry("ubuntu-2404")]))));

            var manifest = service.Build([File("main.tf", "template_name = \"ubuntu-2404\"\n")]);

            Assert.Single(manifest.Disks);
            var warning = Assert.Single(manifest.Warnings);
            Assert.Contains("ISOs on connection 'default' could not be read", warning);
        }

        [Fact]
        public void A_Content_Library_Template_Is_Listed_As_A_VM_Template()
        {
            var service = new DiskManifestService(Inventory(
                ("default", "vsphere", Snapshot([Entry("win2022")], library: [LibraryItem("Golden", "ubuntu-2404")]))));

            var manifest = service.Build([File("main.tf", "name = \"ubuntu-2404\"\n")]);

            var disk = Assert.Single(manifest.Disks);
            Assert.Equal(DiskKind.VmTemplate, disk.Kind);
            Assert.Equal("Golden/ubuntu-2404", disk.Path);
            Assert.Equal("Golden", disk.Properties["library"]);
        }

        [Fact]
        public void A_Content_Library_That_Could_Not_Be_Read_Is_Reported()
        {
            var snapshot = Snapshot([Entry("win2022")], library: []);
            snapshot.ContentLibrary = InventoryCategoryResult.Unavailable("vCenter returned 403 Forbidden when reading content libraries.");
            var service = new DiskManifestService(Inventory(("default", "vsphere", snapshot)));

            var manifest = service.Build([File("main.tf", "template_name = \"win2022\"\n")]);

            Assert.Single(manifest.Disks);
            Assert.Contains(manifest.Warnings, x => x.StartsWith("Content library templates on connection 'default' could not be read") && x.Contains("403"));
        }

        /// <summary>
        /// Templates kept from a read at 09:30 because the latest one failed.
        /// </summary>
        private static InventorySnapshot StaleTemplates(params InventoryEntry[] templates)
        {
            var snapshot = Snapshot([], [Entry(Iso)]);

            snapshot.VmTemplates = new InventoryCategoryResult
            {
                Entries = templates,
                Available = true,
                Stale = true,
                LastUpdated = new DateTime(2026, 10, 24, 9, 30, 0, DateTimeKind.Utc),
                Error = "Could not connect to Proxmox at https://pve.test:8006: Connection refused"
            };

            return snapshot;
        }

        [Fact]
        public void A_Kept_Category_Is_Listed_And_Says_How_Old_It_Is()
        {
            var service = new DiskManifestService(Inventory(("default", "proxmox", StaleTemplates(Entry("ubuntu-2404")))));

            var manifest = service.Build([File("main.tf", "template_name = \"ubuntu-2404\"\n")]);

            var disk = Assert.Single(manifest.Disks);
            Assert.Equal("ubuntu-2404", disk.Name);
            Assert.Equal("default", disk.Connection);
            Assert.Equal("proxmox", disk.Provider);

            var warning = Assert.Single(manifest.Warnings);
            Assert.Contains("VM templates on connection 'default' could not be read", warning);
            Assert.Contains("stale inventory read at 2026-10-24 09:30 UTC", warning);
            Assert.Contains("Connection refused", warning);
        }

        [Fact]
        public void With_Inventory_Disabled_The_Manifest_Is_Empty_And_Says_Why()
        {
            var service = new DiskManifestService(Disabled());

            var manifest = service.Build([File("main.tf", "template_name = \"ubuntu-2404\"\n")]);

            Assert.Empty(manifest.Disks);
            Assert.Contains("not enabled", Assert.Single(manifest.Warnings));
        }

        #endregion

        #region Check

        private static DiskManifest Manifest(params (DiskKind Kind, string Name)[] disks) => new()
        {
            Disks = disks
                .Select(x => new DiskManifestEntry { Kind = x.Kind, Name = x.Name, ReferencedBy = ["main.tf"] })
                .ToList()
        };

        [Fact]
        public void Check_Says_Which_Disks_This_System_Has()
        {
            var service = new DiskManifestService(Inventory(
                ("default", "proxmox", Snapshot([Entry("ubuntu-2404")], [Entry(Iso)]))));

            var disks = service.Check(Manifest(
                (DiskKind.VmTemplate, "ubuntu-2404"),
                (DiskKind.VmTemplate, "win2022"),
                (DiskKind.Iso, Iso)));

            Assert.Equal(
                [DiskStatus.Present, DiskStatus.Missing, DiskStatus.Present],
                disks.Select(x => x.Status));
            Assert.All(disks, x => Assert.Equal(["main.tf"], x.ReferencedBy));
        }

        /// <summary>
        /// A template with the same name as an ISO is not that ISO.
        /// </summary>
        [Fact]
        public void Check_Matches_On_Kind_As_Well_As_Name()
        {
            var service = new DiskManifestService(Inventory(
                ("default", "proxmox", Snapshot([Entry("shared")], []))));

            var disk = Assert.Single(service.Check(Manifest((DiskKind.Iso, "shared"))));

            Assert.Equal(DiskStatus.Missing, disk.Status);
        }

        [Fact]
        public void Check_Cannot_Call_A_Disk_Missing_From_A_Category_It_Could_Not_Read()
        {
            // vSphere cannot list ISOs.
            var service = new DiskManifestService(Inventory(("default", "vsphere", Snapshot([Entry("ubuntu-2404")]))));

            var disk = Assert.Single(service.Check(Manifest((DiskKind.Iso, Iso))));

            Assert.Equal(DiskStatus.Unknown, disk.Status);
        }

        [Fact]
        public void Check_Finds_A_Template_In_A_Content_Library()
        {
            var service = new DiskManifestService(Inventory(
                ("default", "vsphere", Snapshot([Entry("win2022")], library: [LibraryItem("Golden", "ubuntu-2404")]))));

            var disks = service.Check(Manifest((DiskKind.VmTemplate, "ubuntu-2404"), (DiskKind.VmTemplate, "kali-2026")));

            Assert.Equal(DiskStatus.Present, disks.Single(x => x.Name == "ubuntu-2404").Status);
            Assert.Equal(DiskStatus.Missing, disks.Single(x => x.Name == "kali-2026").Status);
        }

        /// <summary>
        /// The folders were read, but the template could be in the library that was not.
        /// </summary>
        [Fact]
        public void Check_Cannot_Call_A_Template_Missing_While_The_Content_Library_Is_Unread()
        {
            var snapshot = Snapshot([Entry("win2022")], library: []);
            snapshot.ContentLibrary = InventoryCategoryResult.Unavailable("vCenter returned 403 Forbidden when reading content libraries.");
            var service = new DiskManifestService(Inventory(("default", "vsphere", snapshot)));

            var disks = service.Check(Manifest((DiskKind.VmTemplate, "win2022"), (DiskKind.VmTemplate, "kali-2026")));

            Assert.Equal(DiskStatus.Present, disks.Single(x => x.Name == "win2022").Status);
            Assert.Equal(DiskStatus.Unknown, disks.Single(x => x.Name == "kali-2026").Status);
        }

        [Fact]
        public void Check_Cannot_Call_A_Template_Missing_While_Another_Connection_Is_Unread()
        {
            var down = Snapshot([]);
            down.VmTemplates = InventoryCategoryResult.Unavailable("Could not connect to Proxmox at https://pve.test:8006: Connection refused");
            var service = new DiskManifestService(Inventory(
                ("default", "proxmox", Snapshot([Entry("ubuntu-2404")])),
                ("lab", "proxmox", down)));

            var disk = Assert.Single(service.Check(Manifest((DiskKind.VmTemplate, "kali-2026"))));

            Assert.Equal(DiskStatus.Unknown, disk.Status);
        }

        [Fact]
        public void Check_Trusts_A_Kept_Category_For_Present_But_Not_For_Missing()
        {
            var service = new DiskManifestService(Inventory(("default", "proxmox", StaleTemplates(Entry("ubuntu-2404")))));

            var disks = service.Check(Manifest((DiskKind.VmTemplate, "ubuntu-2404"), (DiskKind.VmTemplate, "kali-2026")));

            Assert.Equal(DiskStatus.Present, disks.Single(x => x.Name == "ubuntu-2404").Status);
            Assert.Equal(DiskStatus.Unknown, disks.Single(x => x.Name == "kali-2026").Status);
        }

        [Fact]
        public void Check_With_Inventory_Disabled_Reports_Every_Disk_As_Unknown()
        {
            var service = new DiskManifestService(Disabled());

            var disks = service.Check(Manifest((DiskKind.VmTemplate, "ubuntu-2404"), (DiskKind.Iso, Iso)));

            Assert.All(disks, x => Assert.Equal(DiskStatus.Unknown, x.Status));
        }

        /// <summary>
        /// The export lists a disk once per connection it was found on. The target only needs
        /// to be told about it once.
        /// </summary>
        [Fact]
        public void Check_Lists_A_Disk_Found_On_Two_Source_Connections_Once()
        {
            var service = new DiskManifestService(Inventory(("default", "vsphere", Snapshot([]))));
            var manifest = new DiskManifest
            {
                Disks =
                [
                    new() { Kind = DiskKind.VmTemplate, Name = "ubuntu-2404", Connection = "a", ReferencedBy = ["a.tf"] },
                    new() { Kind = DiskKind.VmTemplate, Name = "ubuntu-2404", Connection = "b", ReferencedBy = ["a.tf", "b.tf"] }
                ]
            };

            var disk = Assert.Single(service.Check(manifest));

            Assert.Equal(["a.tf", "b.tf"], disk.ReferencedBy);
        }

        [Fact]
        public void Check_Of_No_Manifest_Is_Empty()
        {
            Assert.Empty(new DiskManifestService(Disabled()).Check(null));
        }

        #endregion
    }
}
