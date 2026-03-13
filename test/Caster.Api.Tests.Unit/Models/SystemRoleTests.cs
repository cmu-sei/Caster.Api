// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Domain.Models;
using Xunit;

namespace Caster.Api.Tests.Unit.Models
{
    [Trait("Category", "Unit")]
    public class SystemRoleTests
    {
        [Theory]
        [InlineData("f35e8fff-f996-4cba-b303-3ba515ad8d2f")]
        public void AdministratorRoleId_WhenAccessed_ReturnsExpectedGuid(string expectedGuid)
        {
            Assert.Equal(new Guid(expectedGuid), SystemRoleDefaults.AdministratorRoleId);
        }

        [Theory]
        [InlineData("d80b73c3-95d7-4468-8650-c62bbd082507")]
        public void ContentDeveloperRoleId_WhenAccessed_ReturnsExpectedGuid(string expectedGuid)
        {
            Assert.Equal(new Guid(expectedGuid), SystemRoleDefaults.ContentDeveloperRoleId);
        }

        [Theory]
        [InlineData("1da3027e-725d-4753-9455-a836ed9bdb1e")]
        public void ObserverRoleId_WhenAccessed_ReturnsExpectedGuid(string expectedGuid)
        {
            Assert.Equal(new Guid(expectedGuid), SystemRoleDefaults.ObserverRoleId);
        }

        [Fact]
        public void GetValues_WhenCalledForSystemPermission_ContainsAllExpectedValues()
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
        public void GetValues_WhenCalledForProjectPermission_ContainsAllExpectedValues()
        {
            var permissions = Enum.GetValues<ProjectPermission>();

            Assert.Contains(ProjectPermission.ViewProject, permissions);
            Assert.Contains(ProjectPermission.EditProject, permissions);
            Assert.Contains(ProjectPermission.ManageProject, permissions);
            Assert.Contains(ProjectPermission.ImportProject, permissions);
            Assert.Contains(ProjectPermission.LockFiles, permissions);
        }

        [Fact]
        public void ProjectRoleDefaults_WhenAccessed_AllRoleIdsAreDefined()
        {
            Assert.NotEqual(Guid.Empty, ProjectRoleDefaults.ProjectCreatorRoleId);
            Assert.NotEqual(Guid.Empty, ProjectRoleDefaults.ProjectReadOnlyRoleId);
            Assert.NotEqual(Guid.Empty, ProjectRoleDefaults.ProjectMemberRoleId);
        }
    }
}
