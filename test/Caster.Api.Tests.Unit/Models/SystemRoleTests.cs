// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Domain.Models;
using TUnit.Core;

namespace Caster.Api.Tests.Unit.Models
{
    [Category("Unit")]
    public class SystemRoleTests
    {
        [Test]
        [Arguments("f35e8fff-f996-4cba-b303-3ba515ad8d2f")]
        public async Task AdministratorRoleId_WhenAccessed_ReturnsExpectedGuid(string expectedGuid)
        {
            await Assert.That(SystemRoleDefaults.AdministratorRoleId).IsEqualTo(new Guid(expectedGuid));
        }

        [Test]
        [Arguments("d80b73c3-95d7-4468-8650-c62bbd082507")]
        public async Task ContentDeveloperRoleId_WhenAccessed_ReturnsExpectedGuid(string expectedGuid)
        {
            await Assert.That(SystemRoleDefaults.ContentDeveloperRoleId).IsEqualTo(new Guid(expectedGuid));
        }

        [Test]
        [Arguments("1da3027e-725d-4753-9455-a836ed9bdb1e")]
        public async Task ObserverRoleId_WhenAccessed_ReturnsExpectedGuid(string expectedGuid)
        {
            await Assert.That(SystemRoleDefaults.ObserverRoleId).IsEqualTo(new Guid(expectedGuid));
        }

        [Test]
        public async Task GetValues_WhenCalledForSystemPermission_ContainsAllExpectedValues()
        {
            var permissions = Enum.GetValues<SystemPermission>();

            await Assert.That(permissions).Contains(SystemPermission.CreateProjects);
            await Assert.That(permissions).Contains(SystemPermission.ViewProjects);
            await Assert.That(permissions).Contains(SystemPermission.EditProjects);
            await Assert.That(permissions).Contains(SystemPermission.ManageProjects);
            await Assert.That(permissions).Contains(SystemPermission.LockFiles);
            await Assert.That(permissions).Contains(SystemPermission.ViewUsers);
            await Assert.That(permissions).Contains(SystemPermission.ManageUsers);
            await Assert.That(permissions).Contains(SystemPermission.ViewWorkspaces);
            await Assert.That(permissions).Contains(SystemPermission.ManageWorkspaces);
            await Assert.That(permissions).Contains(SystemPermission.ViewVLANs);
            await Assert.That(permissions).Contains(SystemPermission.ManageVLANs);
        }

        [Test]
        public async Task GetValues_WhenCalledForProjectPermission_ContainsAllExpectedValues()
        {
            var permissions = Enum.GetValues<ProjectPermission>();

            await Assert.That(permissions).Contains(ProjectPermission.ViewProject);
            await Assert.That(permissions).Contains(ProjectPermission.EditProject);
            await Assert.That(permissions).Contains(ProjectPermission.ManageProject);
            await Assert.That(permissions).Contains(ProjectPermission.ImportProject);
            await Assert.That(permissions).Contains(ProjectPermission.LockFiles);
        }

        [Test]
        public async Task ProjectRoleDefaults_WhenAccessed_AllRoleIdsAreDefined()
        {
            await Assert.That(ProjectRoleDefaults.ProjectCreatorRoleId).IsNotEqualTo(Guid.Empty);
            await Assert.That(ProjectRoleDefaults.ProjectReadOnlyRoleId).IsNotEqualTo(Guid.Empty);
            await Assert.That(ProjectRoleDefaults.ProjectMemberRoleId).IsNotEqualTo(Guid.Empty);
        }
    }
}
