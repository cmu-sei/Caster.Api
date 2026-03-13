// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using AutoMapper;
using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace Caster.Api.Tests.Unit.Mapping
{
    [Category("Unit")]
    [Category("Mapping")]
    public class MappingConfigurationTests
    {
        [Test]
        public async Task ProjectMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Projects.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task ProjectMapping_CreateCommand_MapsToProject()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Projects.MappingProfile>());
            var mapper = config.CreateMapper();

            var command = new Features.Projects.Create.Command { Name = "Test" };
            var result = mapper.Map<Domain.Models.Project>(command);

            await Assert.That(result.Name).IsEqualTo("Test");
        }

        [Test]
        public async Task ProjectMapping_DomainToDto_MapsCorrectly()
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
        public async Task ProjectMapping_EditCommand_MapsToExistingProject()
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
        public async Task DirectoryMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Directories.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task WorkspaceMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Workspaces.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task RunMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Runs.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task PlanMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Plans.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task ApplyMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Applies.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task FileMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Files.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task UserMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Users.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task HostMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Hosts.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task ModuleMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Modules.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task DesignMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Designs.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task DesignModuleMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.DesignModules.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task VariableMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Variables.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task GroupMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Groups.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task SystemRoleMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.SystemRoles.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task ProjectRoleMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.ProjectRoles.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task VlanMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Vlan.MappingProfile>());
            var mapper = config.CreateMapper();

            await Assert.That(mapper).IsNotNull();
        }

        [Test]
        public async Task AllMappingProfiles_CanBeLoadedTogether()
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
