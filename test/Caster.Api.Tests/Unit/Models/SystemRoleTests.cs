// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using Caster.Api.Domain.Models;
using Xunit;

namespace Caster.Api.Tests.Unit.Models
{
    [Trait("Category", "Unit")]
    [Trait("Category", "SystemRole")]
    public class SystemRoleTests
    {
        [Fact]
        public void SystemRoleDefaults_AdministratorRoleId_IsExpectedGuid()
        {
            Assert.Equal(new Guid("f35e8fff-f996-4cba-b303-3ba515ad8d2f"), SystemRoleDefaults.AdministratorRoleId);
        }

        [Fact]
        public void SystemRoleDefaults_ContentDeveloperRoleId_IsExpectedGuid()
        {
            Assert.Equal(new Guid("d80b73c3-95d7-4468-8650-c62bbd082507"), SystemRoleDefaults.ContentDeveloperRoleId);
        }

        [Fact]
        public void SystemRoleDefaults_ObserverRoleId_IsExpectedGuid()
        {
            Assert.Equal(new Guid("1da3027e-725d-4753-9455-a836ed9bdb1e"), SystemRoleDefaults.ObserverRoleId);
        }

        [Fact]
        public void SystemPermission_ContainsAllExpectedValues()
        {
            var permissions = Enum.GetValues<SystemPermission>();

            Assert.Contains(SystemPermission.CreateProjects, permissions);
            Assert.Contains(SystemPermission.ViewProjects, permissions);
            Assert.Contains(SystemPermission.EditProjects, permissions);
            Assert.Contains(SystemPermission.ManageProjects, permissions);
            Assert.Contains(SystemPermission.LockFiles, permissions);
            Assert.Contains(SystemPermission.ViewUsers, permissions);
            Assert.Contains(SystemPermission.ManageUsers, permissions);
            Assert.Contains(SystemPermission.ViewWorkspaces, permissions);
            Assert.Contains(SystemPermission.ManageWorkspaces, permissions);
            Assert.Contains(SystemPermission.ViewVLANs, permissions);
            Assert.Contains(SystemPermission.ManageVLANs, permissions);
        }

        [Fact]
        public void ProjectPermission_ContainsAllExpectedValues()
        {
            var permissions = Enum.GetValues<ProjectPermission>();

            Assert.Contains(ProjectPermission.ViewProject, permissions);
            Assert.Contains(ProjectPermission.EditProject, permissions);
            Assert.Contains(ProjectPermission.ManageProject, permissions);
            Assert.Contains(ProjectPermission.ImportProject, permissions);
            Assert.Contains(ProjectPermission.LockFiles, permissions);
        }

        [Fact]
        public void ProjectRoleDefaults_RoleIds_AreDefined()
        {
            Assert.NotEqual(Guid.Empty, ProjectRoleDefaults.ProjectCreatorRoleId);
            Assert.NotEqual(Guid.Empty, ProjectRoleDefaults.ProjectReadOnlyRoleId);
            Assert.NotEqual(Guid.Empty, ProjectRoleDefaults.ProjectMemberRoleId);
        }
    }
}
