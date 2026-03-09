// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Features.Projects;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Caster.Api.Tests.Unit.Handlers
{
    [Trait("Category", "Unit")]
    [Trait("Category", "ProjectHandlers")]
    public class ProjectHandlerTests : IDisposable
    {
        private readonly CasterContext _dbContext;
        private readonly IMapper _mapper;
        private readonly ICasterAuthorizationService _authService;
        private readonly IIdentityResolver _identityResolver;
        private readonly TelemetryService _telemetryService;

        public ProjectHandlerTests()
        {
            var options = new DbContextOptionsBuilder<CasterContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new CasterContext(options);

            var config = new MapperConfiguration(cfg =>
            {
                cfg.AddProfile<MappingProfile>();
            });
            _mapper = config.CreateMapper();

            _authService = Substitute.For<ICasterAuthorizationService>();
            _identityResolver = Substitute.For<IIdentityResolver>();
            _telemetryService = new TelemetryService();

            // Default: authorize everything
            _authService
                .Authorize(Arg.Any<SystemPermission[]>(), Arg.Any<CancellationToken>())
                .Returns(true);

            _authService
                .Authorize<Domain.Models.Project>(Arg.Any<Guid?>(), Arg.Any<SystemPermission[]>(), Arg.Any<ProjectPermission[]>(), Arg.Any<CancellationToken>())
                .Returns(true);

            // Setup identity
            var userId = Guid.NewGuid();
            var claims = new[] { new Claim("sub", userId.ToString()) };
            var identity = new ClaimsIdentity(claims, "test");
            var principal = new ClaimsPrincipal(identity);
            _identityResolver.GetClaimsPrincipal().Returns(principal);
        }

        public void Dispose()
        {
            _dbContext.Database.EnsureDeleted();
            _dbContext.Dispose();
        }

        #region Create Tests

        [Fact]
        public async Task Create_WithValidCommand_ReturnsProject()
        {
            var handler = new Create.Handler(_authService, _mapper, _dbContext, _telemetryService, _identityResolver);
            var command = new Create.Command { Name = "Test Project" };

            var result = await handler.Handle(command, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal("Test Project", result.Name);
            Assert.NotEqual(Guid.Empty, result.Id);
        }

        [Fact]
        public async Task Create_PersistsProjectToDatabase()
        {
            var handler = new Create.Handler(_authService, _mapper, _dbContext, _telemetryService, _identityResolver);
            var command = new Create.Command { Name = "Persisted Project" };

            var result = await handler.Handle(command, CancellationToken.None);

            var dbProject = await _dbContext.Projects.FindAsync(result.Id);
            Assert.NotNull(dbProject);
            Assert.Equal("Persisted Project", dbProject.Name);
        }

        [Fact]
        public async Task Create_CreatesProjectMembership()
        {
            var handler = new Create.Handler(_authService, _mapper, _dbContext, _telemetryService, _identityResolver);
            var command = new Create.Command { Name = "Project With Membership" };

            var result = await handler.Handle(command, CancellationToken.None);

            var membership = await _dbContext.ProjectMemberships
                .FirstOrDefaultAsync(m => m.ProjectId == result.Id);
            Assert.NotNull(membership);
            Assert.Equal(ProjectRoleDefaults.ProjectCreatorRoleId, membership.RoleId);
        }

        [Fact]
        public async Task Create_WhenNotAuthorized_ThrowsForbiddenException()
        {
            _authService
                .Authorize(Arg.Any<SystemPermission[]>(), Arg.Any<CancellationToken>())
                .Returns(false);

            var handler = new Create.Handler(_authService, _mapper, _dbContext, _telemetryService, _identityResolver);
            var command = new Create.Command { Name = "Unauthorized Project" };

            await Assert.ThrowsAsync<ForbiddenException>(() =>
                handler.Handle(command, CancellationToken.None));
        }

        #endregion

        #region Get Tests

        [Fact]
        public async Task Get_WithExistingProject_ReturnsProject()
        {
            var project = new Domain.Models.Project("Existing Project");
            _dbContext.Projects.Add(project);
            await _dbContext.SaveChangesAsync();

            var handler = new Get.Handler(_authService, _mapper, _dbContext);
            var query = new Get.Query { Id = project.Id };

            var result = await handler.Handle(query, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal("Existing Project", result.Name);
            Assert.Equal(project.Id, result.Id);
        }

        [Fact]
        public async Task Get_WithNonExistentProject_ThrowsEntityNotFoundException()
        {
            var handler = new Get.Handler(_authService, _mapper, _dbContext);
            var query = new Get.Query { Id = Guid.NewGuid() };

            await Assert.ThrowsAsync<EntityNotFoundException<Features.Projects.Project>>(() =>
                handler.Handle(query, CancellationToken.None));
        }

        [Fact]
        public async Task Get_WhenNotAuthorized_ThrowsForbiddenException()
        {
            _authService
                .Authorize<Domain.Models.Project>(Arg.Any<Guid?>(), Arg.Any<SystemPermission[]>(), Arg.Any<ProjectPermission[]>(), Arg.Any<CancellationToken>())
                .Returns(false);

            var handler = new Get.Handler(_authService, _mapper, _dbContext);
            var query = new Get.Query { Id = Guid.NewGuid() };

            await Assert.ThrowsAsync<ForbiddenException>(() =>
                handler.Handle(query, CancellationToken.None));
        }

        #endregion

        #region GetAll Tests

        [Fact]
        public async Task GetAll_ReturnsAllProjects()
        {
            _dbContext.Projects.Add(new Domain.Models.Project("Project 1"));
            _dbContext.Projects.Add(new Domain.Models.Project("Project 2"));
            await _dbContext.SaveChangesAsync();

            var handler = new GetAll.Handler(_authService, _mapper, _dbContext);
            var query = new GetAll.Query { OnlyMine = false };

            var result = await handler.Handle(query, CancellationToken.None);

            Assert.Equal(2, result.Length);
        }

        [Fact]
        public async Task GetAll_WhenOnlyMineTrue_AlwaysAuthorizes()
        {
            var handler = new GetAll.Handler(_authService, _mapper, _dbContext);
            var query = new GetAll.Query { OnlyMine = true };

            _authService.GetAuthorizedProjectIds().Returns(Array.Empty<Guid>());

            var result = await handler.Handle(query, CancellationToken.None);

            Assert.Empty(result);
        }

        [Fact]
        public async Task GetAll_WhenOnlyMineFalseAndUnauthorized_ThrowsForbiddenException()
        {
            _authService
                .Authorize(Arg.Any<SystemPermission[]>(), Arg.Any<CancellationToken>())
                .Returns(false);

            var handler = new GetAll.Handler(_authService, _mapper, _dbContext);
            var query = new GetAll.Query { OnlyMine = false };

            await Assert.ThrowsAsync<ForbiddenException>(() =>
                handler.Handle(query, CancellationToken.None));
        }

        #endregion

        #region Edit Tests

        [Fact]
        public async Task Edit_WithExistingProject_UpdatesName()
        {
            var project = new Domain.Models.Project("Original Name");
            _dbContext.Projects.Add(project);
            await _dbContext.SaveChangesAsync();

            var handler = new Edit.Handler(_authService, _mapper, _dbContext);
            var command = new Edit.Command { Id = project.Id, Name = "Updated Name" };

            var result = await handler.Handle(command, CancellationToken.None);

            Assert.Equal("Updated Name", result.Name);
            Assert.Equal(project.Id, result.Id);
        }

        [Fact]
        public async Task Edit_WithNonExistentProject_ThrowsEntityNotFoundException()
        {
            var handler = new Edit.Handler(_authService, _mapper, _dbContext);
            var command = new Edit.Command { Id = Guid.NewGuid(), Name = "Updated" };

            await Assert.ThrowsAsync<EntityNotFoundException<Features.Projects.Project>>(() =>
                handler.Handle(command, CancellationToken.None));
        }

        [Fact]
        public async Task Edit_PersistsChangesToDatabase()
        {
            var project = new Domain.Models.Project("Original");
            _dbContext.Projects.Add(project);
            await _dbContext.SaveChangesAsync();

            var handler = new Edit.Handler(_authService, _mapper, _dbContext);
            var command = new Edit.Command { Id = project.Id, Name = "Persisted Update" };

            await handler.Handle(command, CancellationToken.None);

            var dbProject = await _dbContext.Projects.FindAsync(project.Id);
            Assert.Equal("Persisted Update", dbProject.Name);
        }

        #endregion

        #region Delete Tests

        [Fact]
        public async Task Delete_WithExistingProject_RemovesFromDatabase()
        {
            var project = new Domain.Models.Project("To Delete");
            _dbContext.Projects.Add(project);
            await _dbContext.SaveChangesAsync();

            var handler = new Delete.Handler(_authService, _telemetryService, _dbContext);
            var command = new Delete.Command { Id = project.Id };

            await handler.Handle(command, CancellationToken.None);

            var dbProject = await _dbContext.Projects.FindAsync(project.Id);
            Assert.Null(dbProject);
        }

        [Fact]
        public async Task Delete_WithNonExistentProject_ThrowsEntityNotFoundException()
        {
            var handler = new Delete.Handler(_authService, _telemetryService, _dbContext);
            var command = new Delete.Command { Id = Guid.NewGuid() };

            await Assert.ThrowsAsync<EntityNotFoundException<Features.Projects.Project>>(() =>
                handler.Handle(command, CancellationToken.None));
        }

        [Fact]
        public async Task Delete_WhenNotAuthorized_ThrowsForbiddenException()
        {
            _authService
                .Authorize<Domain.Models.Project>(Arg.Any<Guid?>(), Arg.Any<SystemPermission[]>(), Arg.Any<ProjectPermission[]>(), Arg.Any<CancellationToken>())
                .Returns(false);

            var handler = new Delete.Handler(_authService, _telemetryService, _dbContext);
            var command = new Delete.Command { Id = Guid.NewGuid() };

            await Assert.ThrowsAsync<ForbiddenException>(() =>
                handler.Handle(command, CancellationToken.None));
        }

        #endregion
    }
}
