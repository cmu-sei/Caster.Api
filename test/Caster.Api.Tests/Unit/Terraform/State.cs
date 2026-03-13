// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Text.Json;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Serialization;
using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using File = System.IO.File;
using Path = System.IO.Path;

namespace Caster.Api.Tests.Unit
{
    [Category("Unit")]
    [Category("TerraformState")]
    [ClassDataSource<StateFixture>(Shared = SharedType.PerTestSession)]
    public class TerraformStateUnitTest(StateFixture stateFixture)
    {
        private readonly StateFixture _stateFixture = stateFixture;

        [Test]
        public async Task Test_Resource_Count()
        {
            await Assert.That(_stateFixture.GetResources().Length).IsEqualTo(13);
        }

        #region Networks

        [Test]
        public async Task Test_vSphere_Host_Port_Groups()
        {
            await this.VerifyHostPortGroup("course-ext-4c2eb68c-a77f-45aa-990a-6b837ee59d71", "tf-HostPortGroup:host-87:course-ext-4c2eb68c-a77f-45aa-990a-6b837ee59d71", "course-ext");
            await this.VerifyHostPortGroup("course-4c2eb68c-a77f-45aa-990a-6b837ee59d71", "tf-HostPortGroup:host-87:course-4c2eb68c-a77f-45aa-990a-6b837ee59d71", "course");
            await this.VerifyHostPortGroup("course-net-4c2eb68c-a77f-45aa-990a-6b837ee59d71", "tf-HostPortGroup:host-87:course-net-4c2eb68c-a77f-45aa-990a-6b837ee59d71", "course-net");
        }

        private async Task VerifyHostPortGroup(string name, string id, string addressName)
        {
            var network = _stateFixture.GetResources().Where(r => r.Id == id).FirstOrDefault();
            await Assert.That(network).IsNotNull();
            await Assert.That(network.Name).IsEqualTo(name);
            await Assert.That(network.Type).IsEqualTo("vsphere_host_port_group");
            await Assert.That(network.Address).IsEqualTo($"vsphere_host_port_group.{addressName}");
            await Assert.That(network.BaseAddress).IsEqualTo($"vsphere_host_port_group.{addressName}");
        }

        #endregion

        [Test]
        public async Task Test_vSphere_Virtual_Switches()
        {
            var vSwitch = _stateFixture.GetResources().Where(r => r.Id == "tf-HostVirtualSwitch:host-87:vSwitch-4c2eb68c-a77f-45aa-990a").FirstOrDefault();
            await Assert.That(vSwitch).IsNotNull();
            await Assert.That(vSwitch.Name).IsEqualTo("vSwitch-4c2eb68c-a77f-45aa-990a");
            await Assert.That(vSwitch.Type).IsEqualTo("vsphere_host_virtual_switch");
            await Assert.That(vSwitch.Address).IsEqualTo("vsphere_host_virtual_switch.switch");
            await Assert.That(vSwitch.BaseAddress).IsEqualTo("vsphere_host_virtual_switch.switch");
        }

        #region vSphere_Virtual_Machines
        [Test]
        public async Task Test_vSphere_Virtual_Machines()
        {
            await this.VerifyVirtualMachine(
                "course.centos6.student.1.4c2eb68c-a77f-45aa-990a-6b837ee59d71",
                "423ccd67-a76a-ea9f-7089-caa891a621f2",
                "course-centos6-student",
                new Guid("925e2634-52b5-4492-ba5e-c800fe3401f1"),
                0);

            await this.VerifyVirtualMachine(
                "course.centos6.student.2.4c2eb68c-a77f-45aa-990a-6b837ee59d71",
                "423c087c-4715-bfca-2475-9cadb6954f2e",
                "course-centos6-student",
                new Guid("925e2634-52b5-4492-ba5e-c800fe3401f1"),
                1);

            await this.VerifyVirtualMachine(
                "course.centos7.server-4c2eb68c-a77f-45aa-990a-6b837ee59d71",
                "423c3932-665f-5764-ce9a-aa4a8d8a2023",
                "course-centos7-server",
                null,
                null);

            await this.VerifyVirtualMachine(
                "course.freebsd10-4c2eb68c-a77f-45aa-990a-6b837ee59d71",
                "423c0a62-8566-950c-f5e9-cc4cdf5660be",
                "course-freebsd10-ws01",
                null,
                null);

            await this.VerifyVirtualMachine(
                "course.freebsd9-4c2eb68c-a77f-45aa-990a-6b837ee59d71",
                "423c0fb1-220d-7da5-9310-4fdefe4024a4",
                "course-freebsd9-ws01",
                null,
                null);

            await this.VerifyVirtualMachine(
                "course.sol10-4c2eb68c-a77f-45aa-990a-6b837ee59d71",
                "423cb77b-6789-cef5-69e2-9b29b2b2b252",
                "course-sol10-ws01",
                null,
                null);

            await this.VerifyVirtualMachine(
                "course.sol11-4c2eb68c-a77f-45aa-990a-6b837ee59d71",
                "423cbe4b-761d-f479-13ec-ca4adc5e7b36",
                "course-sol11-ws01",
                null,
                null);

            await this.VerifyVirtualMachine(
                "course.ubuntu14.server-4c2eb68c-a77f-45aa-990a-6b837ee59d71",
                "423c09a2-5bd4-1568-3dd7-7fe81512c101",
                "course-ubuntu14-server",
                null,
                null);

            await this.VerifyVirtualMachine(
                "course.ubuntu16.server-4c2eb68c-a77f-45aa-990a-6b837ee59d71",
                "423cf12c-013b-f590-3be8-f53a3115ab92",
                "course-ubuntu16-server",
                null,
                null);
        }

        private async Task VerifyVirtualMachine(string name, string id, string addressName, Guid? teamId, int? count)
        {
            var machine = _stateFixture.GetResources().Where(r => r.Id == id).FirstOrDefault();
            await Assert.That(machine).IsNotNull();
            await Assert.That(machine.Name).IsEqualTo(name);
            await Assert.That(machine.Type).IsEqualTo("vsphere_virtual_machine");
            await Assert.That(machine.Address).IsEqualTo($"vsphere_virtual_machine.{addressName}{(count.HasValue ? string.Format("[{0}]", count) : "")}");
            await Assert.That(machine.BaseAddress).IsEqualTo($"vsphere_virtual_machine.{addressName}");
            await Assert.That(machine.GetTeamIds()?.FirstOrDefault()).IsEqualTo(teamId);
        }

        [Test]
        public async Task Test_Resource_Searchable()
        {
            var resources = _stateFixture.GetResources();

            var machine = resources.Where(r => r.Id == "423c087c-4715-bfca-2475-9cadb6954f2e").FirstOrDefault();
            var searchable = machine.SearchableAttributes;
            var json = JsonSerializer.Serialize(machine, DefaultJsonSettings.Settings);

            var portgroup = resources.Where(r => r.Type == "vsphere_host_port_group").FirstOrDefault();
            json = JsonSerializer.Serialize(portgroup, DefaultJsonSettings.Settings);
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
            _rawState = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "Data", "terraform.tfstate"));
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
