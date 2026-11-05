// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using Caster.Api.Domain.Services.Inventory;
using VimClient;
using Xunit;

namespace Caster.Api.Tests.Domain.Services.Inventory
{
    /// <summary>
    /// The parts of the vim25 reader that need no vCenter: turning property
    /// collector results into objects, and working out where to search for ISOs.
    /// </summary>
    [Trait("Category", "Unit")]
    public class VimInventorySourceTests
    {
        private static ObjectContent Object(string type, string id, params (string Name, object Value)[] properties) => new()
        {
            obj = new ManagedObjectReference { type = type, Value = id },
            propSet = properties.Select(x => new DynamicProperty { name = x.Name, val = x.Value }).ToArray()
        };

        private static ManagedObjectReference Ref(string type, string id) => new() { type = type, Value = id };

        [Fact]
        public void Project_ReadsEachKindOfObject()
        {
            var inventory = VimInventorySource.Project(
            [
                Object("Datacenter", "datacenter-1", ("name", "DC1"), ("vmFolder", Ref("Folder", "group-v1"))),
                Object("Folder", "group-v2", ("name", "Templates"), ("parent", Ref("Folder", "group-v1"))),
                Object("VirtualMachine", "vm-2",
                    ("name", "ubuntu"), ("config.template", true), ("runtime.powerState", VirtualMachinePowerState.poweredOff),
                    ("config.hardware.numCPU", 2), ("config.hardware.memoryMB", 4096),
                    ("parent", Ref("Folder", "group-v2")), ("runtime.host", Ref("HostSystem", "host-1"))),
                Object("HostSystem", "host-1",
                    ("name", "esx1"), ("runtime.connectionState", HostSystemConnectionState.connected),
                    ("runtime.powerState", HostSystemPowerState.poweredOn),
                    ("summary.quickStats.overallCpuUsage", 67), ("summary.quickStats.overallMemoryUsage", 1404),
                    ("summary.hardware.cpuMhz", 2294), ("summary.hardware.numCpuCores", (short)2),
                    ("summary.hardware.memorySize", 4294430720L), ("parent", Ref("ClusterComputeResource", "domain-c1"))),
                Object("ClusterComputeResource", "domain-c1", ("name", "Cluster"), ("parent", Ref("Folder", "group-h1"))),
                Object("Datastore", "datastore-1",
                    ("summary.name", "LocalDS_0"), ("summary.type", "OTHER"), ("summary.capacity", 100L),
                    ("summary.freeSpace", 40L), ("summary.accessible", true), ("browser", Ref("HostDatastoreBrowser", "browser-1")),
                    ("parent", Ref("Folder", "group-s1"))),
                Object("Network", "network-1", ("name", "VM Network"), ("parent", Ref("Folder", "group-n1"))),
                Object("ResourcePool", "resgroup-1", ("name", "Resources"))
            ]);

            Assert.Equal(new VimDatacenter("datacenter-1", "DC1", "group-v1"), Assert.Single(inventory.Datacenters));
            Assert.Equal(new VimFolder("group-v2", "Templates", "group-v1"), Assert.Single(inventory.Folders));
            Assert.Equal(new VimVm("vm-2", "ubuntu", true, "poweredOff", 2, 4096, "group-v2", "host-1"), Assert.Single(inventory.Vms));
            Assert.Equal(new VimHost("host-1", "esx1", "connected", "poweredOn", 67, 2294, 2, 1404, 4294430720L, "domain-c1"), Assert.Single(inventory.Hosts));
            Assert.Equal(new VimFolder("domain-c1", "Cluster", "group-h1"), Assert.Single(inventory.ComputeResources));
            Assert.Equal(new VimDatastore("datastore-1", "LocalDS_0", "OTHER", 100, 40, true, "browser-1", "group-s1"), Assert.Single(inventory.Datastores));
            Assert.Equal(new VimNetwork("network-1", "VM Network", "Network", false, "group-n1"), Assert.Single(inventory.Networks));
        }

        [Fact]
        public void Project_ReadsAMissingTemplateFlagAsNotATemplate()
        {
            // A VM whose config is not readable (e.g. mid-creation) has no config.template.
            var inventory = VimInventorySource.Project([Object("VirtualMachine", "vm-1", ("name", "new-vm"))]);

            Assert.False(Assert.Single(inventory.Vms).Template);
        }

        [Fact]
        public void Project_KeepsAPortgroupMatchedTwiceOnce_AsAnUplinkWhenItIsOne()
        {
            // The Network and the DistributedVirtualPortgroup property specs both match a portgroup.
            var inventory = VimInventorySource.Project(
            [
                Object("DistributedVirtualPortgroup", "dvportgroup-1", ("name", "Uplinks")),
                Object("DistributedVirtualPortgroup", "dvportgroup-1", ("name", "Uplinks"), ("config.uplink", true)),
                Object("Network", "network-1", ("name", "VM Network"))
            ]);

            Assert.Equal(2, inventory.Networks.Count);
            Assert.True(inventory.Networks.Single(x => x.Id == "dvportgroup-1").Uplink);
            Assert.False(inventory.Networks.Single(x => x.Id == "network-1").Uplink);
        }

        [Fact]
        public void Project_KeepsTheParentOfAPortgroupMatchedTwice()
        {
            var inventory = VimInventorySource.Project(
            [
                Object("DistributedVirtualPortgroup", "dvportgroup-1", ("name", "vlan-200"), ("parent", Ref("Folder", "group-n1"))),
                Object("DistributedVirtualPortgroup", "dvportgroup-1", ("name", "vlan-200")),
                Object("DistributedVirtualPortgroup", "dvportgroup-2", ("name", "Uplinks")),
                Object("DistributedVirtualPortgroup", "dvportgroup-2", ("name", "Uplinks"), ("config.uplink", true), ("parent", Ref("Folder", "group-n1")))
            ]);

            Assert.Equal("group-n1", inventory.Networks.Single(x => x.Id == "dvportgroup-1").Parent);
            Assert.Equal("group-n1", inventory.Networks.Single(x => x.Id == "dvportgroup-2").Parent);
        }

        [Fact]
        public void Project_TakesTheUplinkTagAsAnUplink()
        {
            var inventory = VimInventorySource.Project(
            [
                Object("DistributedVirtualPortgroup", "dvportgroup-1", ("name", "DVS-Uplinks"),
                    ("tag", new[] { new Tag { key = "SYSTEM/DVS.UPLINKPG" } })),
                Object("DistributedVirtualPortgroup", "dvportgroup-2", ("name", "vlan-200"), ("tag", new Tag[0]))
            ]);

            Assert.True(inventory.Networks.Single(x => x.Id == "dvportgroup-1").Uplink);
            Assert.False(inventory.Networks.Single(x => x.Id == "dvportgroup-2").Uplink);
        }

        [Fact]
        public void Project_TakesASwitchsUplinkPortgroupsAsUplinks()
        {
            var inventory = VimInventorySource.Project(
            [
                Object("DistributedVirtualPortgroup", "dvportgroup-1", ("name", "DVS-Uplinks")),
                Object("VmwareDistributedVirtualSwitch", "dvs-1",
                    ("config.uplinkPortgroup", new[] { Ref("DistributedVirtualPortgroup", "dvportgroup-1") })),
                Object("DistributedVirtualPortgroup", "dvportgroup-2", ("name", "vlan-200"))
            ]);

            Assert.True(inventory.Networks.Single(x => x.Id == "dvportgroup-1").Uplink);
            Assert.False(inventory.Networks.Single(x => x.Id == "dvportgroup-2").Uplink);
        }

        [Theory]
        [InlineData("[ds1] iso/", "alpine.iso", "[ds1] iso/alpine.iso")]
        [InlineData("[ds1]/iso", "alpine.iso", "[ds1] iso/alpine.iso")]
        [InlineData("[ds1] iso/linux", "alpine.iso", "[ds1] iso/linux/alpine.iso")]
        [InlineData("[ds1]", "alpine.iso", "[ds1] alpine.iso")]
        [InlineData("[ds1] ", "alpine.iso", "[ds1] alpine.iso")]
        [InlineData(null, "alpine.iso", "[ds1] alpine.iso")]
        public void DatastorePath_IsTheStandardForm(string folderPath, string file, string expected)
        {
            Assert.Equal(expected, VimInventorySource.DatastorePath("ds1", folderPath, file));
        }

        [Fact]
        public void IsoTargets_WithNoLocationsSearchesEveryDatastoreFromItsRoot()
        {
            var (targets, unknown) = VimInventorySource.IsoTargets([], ["b", "a"]);

            Assert.Equal([("a", ""), ("b", "")], targets);
            Assert.Empty(unknown);
        }

        [Fact]
        public void IsoTargets_TakesDatastoreNamesAndDatastorePaths()
        {
            var (targets, unknown) = VimInventorySource.IsoTargets(
                ["iso-ds", "[shared] stock/linux/", " [shared] stock/linux ", "[shared] windows", "", "  "],
                ["iso-ds", "shared", "other"]);

            Assert.Equal([("iso-ds", ""), ("shared", "stock/linux"), ("shared", "windows")], targets);
            Assert.Empty(unknown);
        }

        [Fact]
        public void IsoTargets_ADatastoreRootCoversItsFolders()
        {
            var (targets, _) = VimInventorySource.IsoTargets(["[shared] stock", "shared"], ["shared"]);

            Assert.Equal([("shared", "")], targets);
        }

        [Fact]
        public void IsoTargets_ReportsLocationsThatAreNotAccessibleDatastores()
        {
            var (targets, unknown) = VimInventorySource.IsoTargets(["missing", "[gone] iso", "iso-ds"], ["iso-ds"]);

            Assert.Equal([("iso-ds", "")], targets);
            Assert.Equal(["missing", "[gone] iso"], unknown);
        }
    }
}
