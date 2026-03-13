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
        public void CreateMapper_WithProjectProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Projects.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void Map_CreateCommandToProject_ShouldMapNameCorrectly()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Projects.MappingProfile>());
            var mapper = config.CreateMapper();

            var command = new Features.Projects.Create.Command { Name = "Test" };
            var result = mapper.Map<Domain.Models.Project>(command);

            Assert.Equal("Test", result.Name);
        }

        [Fact]
        public void Map_ProjectDomainToDto_ShouldMapAllFieldsCorrectly()
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
        public void Map_EditCommandToProject_ShouldUpdateExistingProject()
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
        public void CreateMapper_WithDirectoryProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Directories.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void CreateMapper_WithWorkspaceProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Workspaces.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void CreateMapper_WithRunProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Runs.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void CreateMapper_WithPlanProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Plans.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void CreateMapper_WithApplyProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Applies.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void CreateMapper_WithFileProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Files.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void CreateMapper_WithUserProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Users.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void CreateMapper_WithHostProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Hosts.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void CreateMapper_WithModuleProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Modules.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void CreateMapper_WithDesignProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Designs.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void CreateMapper_WithDesignModuleProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.DesignModules.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void CreateMapper_WithVariableProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Variables.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void CreateMapper_WithGroupProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Groups.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void CreateMapper_WithSystemRoleProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.SystemRoles.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void CreateMapper_WithProjectRoleProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.ProjectRoles.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void CreateMapper_WithVlanProfile_ShouldSucceed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.AddProfile<Features.Vlan.MappingProfile>());
            var mapper = config.CreateMapper();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void CreateMapper_WithAllProfiles_ShouldSucceed()
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
