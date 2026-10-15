// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Text.Json;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Serialization;
using File = System.IO.File;
using Path = System.IO.Path;

namespace Caster.Api.Tests.Domain.Models
{
    /// <summary>Reading resources out of a vSphere workspace's Terraform state (<c>Data/terraform.tfstate</c>).</summary>
    public class StateTests : IClassFixture<StateFixture>
    {
        private readonly StateFixture _stateFixture;

        public StateTests(StateFixture stateFixture)
        {
            _stateFixture = stateFixture;
        }

        [Fact]
        public void GetResources_lists_every_resource_instance_in_the_state()
        {
            Assert.Equal(13, _stateFixture.GetResources().Length);
        }

        #region Networks

        [Fact]
        public void Host_port_groups_carry_their_name_type_and_address()
        {
            this.VerifyHostPortGroup("course-ext-4c2eb68c-a77f-45aa-990a-6b837ee59d71", "tf-HostPortGroup:host-87:course-ext-4c2eb68c-a77f-45aa-990a-6b837ee59d71", "course-ext");
            this.VerifyHostPortGroup("course-4c2eb68c-a77f-45aa-990a-6b837ee59d71", "tf-HostPortGroup:host-87:course-4c2eb68c-a77f-45aa-990a-6b837ee59d71", "course");
            this.VerifyHostPortGroup("course-net-4c2eb68c-a77f-45aa-990a-6b837ee59d71", "tf-HostPortGroup:host-87:course-net-4c2eb68c-a77f-45aa-990a-6b837ee59d71", "course-net");
        }

        private void VerifyHostPortGroup(string name, string id, string addressName)
        {
            var network = _stateFixture.GetResources().Where(r => r.Id == id).FirstOrDefault();
            Assert.NotNull(network);
            Assert.Equal(name, network.Name);
            Assert.Equal("vsphere_host_port_group", network.Type);
            Assert.Equal($"vsphere_host_port_group.{addressName}", network.Address);
            Assert.Equal($"vsphere_host_port_group.{addressName}", network.BaseAddress);
        }

        #endregion

        [Fact]
        public void A_virtual_switch_carries_its_name_type_and_address()
        {
            var vSwitch = _stateFixture.GetResources().Where(r => r.Id == "tf-HostVirtualSwitch:host-87:vSwitch-4c2eb68c-a77f-45aa-990a").FirstOrDefault();
            Assert.NotNull(vSwitch);
            Assert.Equal("vSwitch-4c2eb68c-a77f-45aa-990a", vSwitch.Name);
            Assert.Equal("vsphere_host_virtual_switch", vSwitch.Type);
            Assert.Equal("vsphere_host_virtual_switch.switch", vSwitch.Address);
            Assert.Equal("vsphere_host_virtual_switch.switch", vSwitch.BaseAddress);
        }

        #region vSphere_Virtual_Machines
        [Fact]
        public void Virtual_machines_carry_their_name_address_index_and_team()
        {
            this.VerifyVirtualMachine(
                "course.centos6.student.1.4c2eb68c-a77f-45aa-990a-6b837ee59d71",
                "423ccd67-a76a-ea9f-7089-caa891a621f2",
                "course-centos6-student",
                new Guid("925e2634-52b5-4492-ba5e-c800fe3401f1"),
                0);

            this.VerifyVirtualMachine(
                "course.centos6.student.2.4c2eb68c-a77f-45aa-990a-6b837ee59d71",
                "423c087c-4715-bfca-2475-9cadb6954f2e",
                "course-centos6-student",
                new Guid("925e2634-52b5-4492-ba5e-c800fe3401f1"),
                1);

            this.VerifyVirtualMachine(
                "course.centos7.server-4c2eb68c-a77f-45aa-990a-6b837ee59d71",
                "423c3932-665f-5764-ce9a-aa4a8d8a2023",
                "course-centos7-server",
                null,
                null);

            this.VerifyVirtualMachine(
                "course.freebsd10-4c2eb68c-a77f-45aa-990a-6b837ee59d71",
                "423c0a62-8566-950c-f5e9-cc4cdf5660be",
                "course-freebsd10-ws01",
                null,
                null);

            this.VerifyVirtualMachine(
                "course.freebsd9-4c2eb68c-a77f-45aa-990a-6b837ee59d71",
                "423c0fb1-220d-7da5-9310-4fdefe4024a4",
                "course-freebsd9-ws01",
                null,
                null);

            this.VerifyVirtualMachine(
                "course.sol10-4c2eb68c-a77f-45aa-990a-6b837ee59d71",
                "423cb77b-6789-cef5-69e2-9b29b2b2b252",
                "course-sol10-ws01",
                null,
                null);

            this.VerifyVirtualMachine(
                "course.sol11-4c2eb68c-a77f-45aa-990a-6b837ee59d71",
                "423cbe4b-761d-f479-13ec-ca4adc5e7b36",
                "course-sol11-ws01",
                null,
                null);

            this.VerifyVirtualMachine(
                "course.ubuntu14.server-4c2eb68c-a77f-45aa-990a-6b837ee59d71",
                "423c09a2-5bd4-1568-3dd7-7fe81512c101",
                "course-ubuntu14-server",
                null,
                null);

            this.VerifyVirtualMachine(
                "course.ubuntu16.server-4c2eb68c-a77f-45aa-990a-6b837ee59d71",
                "423cf12c-013b-f590-3be8-f53a3115ab92",
                "course-ubuntu16-server",
                null,
                null);
        }

        private void VerifyVirtualMachine(string name, string id, string addressName, Guid? teamId, int? count)
        {
            var machine = _stateFixture.GetResources().Where(r => r.Id == id).FirstOrDefault();
            Assert.NotNull(machine);
            Assert.Equal(name, machine.Name);
            Assert.Equal("vsphere_virtual_machine", machine.Type);
            Assert.Equal($"vsphere_virtual_machine.{addressName}{(count.HasValue ? string.Format("[{0}]", count) : "")}", machine.Address);
            Assert.Equal($"vsphere_virtual_machine.{addressName}", machine.BaseAddress);
            Assert.Equal(teamId, machine.GetTeamIds()?.FirstOrDefault());
        }

        [Fact]
        public void A_resource_serializes_with_its_searchable_attributes()
        {
            var machine = _stateFixture.GetResources().Single(r => r.Id == "423c087c-4715-bfca-2475-9cadb6954f2e");

            var json = JsonSerializer.Serialize(machine, DefaultJsonSettings.Settings);

            Assert.NotEmpty(machine.SearchableAttributes);
            Assert.Contains("423c087c-4715-bfca-2475-9cadb6954f2e", json);
        }

        [Fact]
        public void A_host_port_group_serializes_with_its_vlan_id_as_a_searchable_attribute()
        {
            const string id = "tf-HostPortGroup:host-87:course-4c2eb68c-a77f-45aa-990a-6b837ee59d71";
            var portGroup = _stateFixture.GetResources().Single(r => r.Type == "vsphere_host_port_group" && r.Id == id);

            var json = JsonSerializer.Serialize(portGroup, DefaultJsonSettings.Settings);

            Assert.Equal(["vlan_id"], portGroup.SearchableAttributes.Keys);
            using var document = JsonDocument.Parse(json);
            Assert.Equal(id, document.RootElement.GetProperty("Id").GetString());
            Assert.Equal(0, document.RootElement.GetProperty("SearchableAttributes").GetProperty("vlan_id").GetInt32());
        }

        #endregion
    }

    public class StateFixture
    {
        private readonly string _rawState;
        private readonly Workspace _workspace;
        private readonly State _state;
        private readonly Resource[] _resources;

        public StateFixture()
        {
            _rawState = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "terraform.tfstate"));
            _workspace = new Workspace { State = _rawState };
            _state = _workspace.GetState();
            _resources = _state.GetResources();
        }

        public Resource[] GetResources()
        {
            return _resources;
        }
    }
}
