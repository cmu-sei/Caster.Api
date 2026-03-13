// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using Caster.Api.Domain.Models;
using Directory = Caster.Api.Domain.Models.Directory;
using File = Caster.Api.Domain.Models.File;
using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace Caster.Api.Tests.Unit.Models
{
    [Category("Unit")]
    [Category("SystemRole")]
    public class SystemRoleTests
    {
        [Test]
        public async Task SystemRoleDefaults_AdministratorRoleId_IsExpectedGuid()
        {
            await Assert.That(SystemRoleDefaults.AdministratorRoleId).IsEqualTo(new Guid("f35e8fff-f996-4cba-b303-3ba515ad8d2f"));
        }

        [Test]
        public async Task SystemRoleDefaults_ContentDeveloperRoleId_IsExpectedGuid()
        {
            await Assert.That(SystemRoleDefaults.ContentDeveloperRoleId).IsEqualTo(new Guid("d80b73c3-95d7-4468-8650-c62bbd082507"));
        }

        [Test]
        public async Task SystemRoleDefaults_ObserverRoleId_IsExpectedGuid()
        {
            await Assert.That(SystemRoleDefaults.ObserverRoleId).IsEqualTo(new Guid("1da3027e-725d-4753-9455-a836ed9bdb1e"));
        }

        [Test]
        public async Task SystemPermission_ContainsAllExpectedValues()
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
        public async Task ProjectPermission_ContainsAllExpectedValues()
        {
            var permissions = Enum.GetValues<ProjectPermission>();

            await Assert.That(permissions).Contains(ProjectPermission.ViewProject);
            await Assert.That(permissions).Contains(ProjectPermission.EditProject);
            await Assert.That(permissions).Contains(ProjectPermission.ManageProject);
            await Assert.That(permissions).Contains(ProjectPermission.ImportProject);
            await Assert.That(permissions).Contains(ProjectPermission.LockFiles);
        }

        [Test]
        public async Task ProjectRoleDefaults_RoleIds_AreDefined()
        {
            await Assert.That(ProjectRoleDefaults.ProjectCreatorRoleId).IsNotEqualTo(Guid.Empty);
            await Assert.That(ProjectRoleDefaults.ProjectReadOnlyRoleId).IsNotEqualTo(Guid.Empty);
            await Assert.That(ProjectRoleDefaults.ProjectMemberRoleId).IsNotEqualTo(Guid.Empty);
        }
    }
}
