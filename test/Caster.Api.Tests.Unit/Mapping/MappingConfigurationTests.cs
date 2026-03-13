// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using AutoMapper;
using Xunit;

namespace Caster.Api.Tests.Unit.Mapping
{
    [Trait("Category", "Unit")]
    [Trait("Category", "Mapping")]
    public class MappingConfigurationTests
    {
        [Fact]
        public void ProjectMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Projects.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void ProjectMapping_CreateCommand_MapsToProject()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Projects.MappingProfile>());
            var mapper = config.CreateMapper();

            var command = new Features.Projects.Create.Command { Name = "Test" };
            var result = mapper.Map<Domain.Models.Project>(command);

            Assert.Equal("Test", result.Name);
        }

        [Fact]
        public void ProjectMapping_DomainToDto_MapsCorrectly()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Projects.MappingProfile>());
            var mapper = config.CreateMapper();

            var project = new Domain.Models.Project { Id = Guid.NewGuid(), Name = "TestProject" };
            var result = mapper.Map<Features.Projects.Project>(project);

            Assert.Equal(project.Id, result.Id);
            Assert.Equal("TestProject", result.Name);
        }

        [Fact]
        public void ProjectMapping_EditCommand_MapsToExistingProject()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Projects.MappingProfile>());
            var mapper = config.CreateMapper();

            var existingProject = new Domain.Models.Project { Id = Guid.NewGuid(), Name = "Original" };
            var command = new Features.Projects.Edit.Command { Id = existingProject.Id, Name = "Updated" };
            mapper.Map(command, existingProject);

            Assert.Equal("Updated", existingProject.Name);
        }

        [Fact]
        public void DirectoryMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Directories.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void WorkspaceMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Workspaces.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void RunMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Runs.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void PlanMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Plans.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void ApplyMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Applies.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void FileMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Files.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void UserMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Users.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void HostMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Hosts.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void ModuleMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Modules.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void DesignMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Designs.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void DesignModuleMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.DesignModules.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void VariableMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Variables.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void GroupMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Groups.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void SystemRoleMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.SystemRoles.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void ProjectRoleMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.ProjectRoles.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void VlanMappingProfile_CanBeCreated()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Vlan.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void AllMappingProfiles_CanBeLoadedTogether()
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

            Assert.NotNull(mapper);
        }
    }
}
