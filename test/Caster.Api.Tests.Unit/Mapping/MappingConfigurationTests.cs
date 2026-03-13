// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using AutoMapper;
using TUnit.Core;

namespace Caster.Api.Tests.Unit.Mapping
{
    [Category("Unit")]
    [Category("Mapping")]
    public class MappingConfigurationTests
    {
        [Test]
        public async Task CreateMapper_WithProjectProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Projects.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task Map_CreateCommandToProject_ShouldMapNameCorrectly()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Projects.MappingProfile>());
            var mapper = config.CreateMapper();

            var command = new Features.Projects.Create.Command { Name = "Test" };
            var result = mapper.Map<Domain.Models.Project>(command);

            await Assert.That(result.Name).IsEqualTo("Test");
        }

        [Test]
        public async Task Map_ProjectDomainToDto_ShouldMapAllFieldsCorrectly()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Projects.MappingProfile>());
            var mapper = config.CreateMapper();

            var project = new Domain.Models.Project { Id = Guid.NewGuid(), Name = "TestProject" };
            var result = mapper.Map<Features.Projects.Project>(project);

            await Assert.That(result.Id).IsEqualTo(project.Id);
            await Assert.That(result.Name).IsEqualTo("TestProject");
        }

        [Test]
        public async Task Map_EditCommandToProject_ShouldUpdateExistingProject()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Projects.MappingProfile>());
            var mapper = config.CreateMapper();

            var existingProject = new Domain.Models.Project { Id = Guid.NewGuid(), Name = "Original" };
            var command = new Features.Projects.Edit.Command { Id = existingProject.Id, Name = "Updated" };
            mapper.Map(command, existingProject);

            await Assert.That(existingProject.Name).IsEqualTo("Updated");
        }

        [Test]
        public async Task CreateMapper_WithDirectoryProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Directories.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task CreateMapper_WithWorkspaceProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Workspaces.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task CreateMapper_WithRunProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Runs.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task CreateMapper_WithPlanProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Plans.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task CreateMapper_WithApplyProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Applies.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task CreateMapper_WithFileProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Files.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task CreateMapper_WithUserProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Users.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task CreateMapper_WithHostProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Hosts.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task CreateMapper_WithModuleProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Modules.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task CreateMapper_WithDesignProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Designs.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task CreateMapper_WithDesignModuleProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.DesignModules.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task CreateMapper_WithVariableProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Variables.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task CreateMapper_WithGroupProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Groups.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task CreateMapper_WithSystemRoleProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.SystemRoles.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task CreateMapper_WithProjectRoleProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.ProjectRoles.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task CreateMapper_WithVlanProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Vlan.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task CreateMapper_WithAllProfiles_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.AddProfile<Features.Projects.MappingProfile>();
                cfg.AddProfile<Features.Directories.MappingProfile>();
                cfg.AddProfile<Features.Workspaces.MappingProfile>();
                cfg.AddProfile<Features.Runs.MappingProfile>();
                cfg.AddProfile<Features.Plans.MappingProfile>();
                cfg.AddProfile<Features.Applies.MappingProfile>();
                cfg.AddProfile<Features.Files.MappingProfile>();
                cfg.AddProfile<Features.Users.MappingProfile>();
                cfg.AddProfile<Features.Hosts.MappingProfile>();
                cfg.AddProfile<Features.Modules.MappingProfile>();
                cfg.AddProfile<Features.Designs.MappingProfile>();
                cfg.AddProfile<Features.DesignModules.MappingProfile>();
                cfg.AddProfile<Features.Variables.MappingProfile>();
                cfg.AddProfile<Features.Groups.MappingProfile>();
                cfg.AddProfile<Features.SystemRoles.MappingProfile>();
                cfg.AddProfile<Features.ProjectRoles.MappingProfile>();
                cfg.AddProfile<Features.Vlan.MappingProfile>();
            });
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }
    }
}
