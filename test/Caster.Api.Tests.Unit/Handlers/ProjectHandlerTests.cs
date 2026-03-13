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
using Crucible.Common.Testing.Fixtures;
using FakeItEasy;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Caster.Api.Tests.Unit.Handlers
{
    [Category("Unit")]
    public class ProjectHandlerTests : IDisposable
    {
        private readonly CasterContext _dbContext;
        private readonly IMapper _mapper;
        private readonly ICasterAuthorizationService _authService;
        private readonly IIdentityResolver _identityResolver;
        private readonly TelemetryService _telemetryService;

        public ProjectHandlerTests()
        {
            _dbContext = TestDbContextFactory.Create<CasterContext>();

            var config = new MapperConfiguration(cfg =>
            {
                cfg.AddProfile<MappingProfile>();
            });
            _mapper = config.CreateMapper();

            _authService = A.Fake<ICasterAuthorizationService>();
            _identityResolver = A.Fake<IIdentityResolver>();
            _telemetryService = new TelemetryService();

            // Default: authorize everything
            A.CallTo(() => _authService.Authorize(A<SystemPermission[]>._, A<CancellationToken>._))
                .Returns(true);

            A.CallTo(() => _authService.Authorize<Domain.Models.Project>(A<Guid?>._, A<SystemPermission[]>._, A<ProjectPermission[]>._, A<CancellationToken>._))
                .Returns(true);

            // Setup identity
            var userId = Guid.NewGuid();
            var claims = new[] { new Claim("sub", userId.ToString()) };
            var identity = new ClaimsIdentity(claims, "test");
            var principal = new ClaimsPrincipal(identity);
            A.CallTo(() => _identityResolver.GetClaimsPrincipal()).Returns(principal);
        }

        public void Dispose()
        {
            _dbContext.Database.EnsureDeleted();
            _dbContext.Dispose();
        }

        #region Create Tests

        [Test]
        public async Task Handle_CreateWithValidCommand_ReturnsProject()
        {
            var handler = new Create.Handler(_authService, _mapper, _dbContext, _telemetryService, _identityResolver);
            var command = new Create.Command { Name = "Test Project" };

            var result = await handler.Handle(command, CancellationToken.None);

            await Assert.That(result).IsNotNull();
            await Assert.That(result.Name).IsEqualTo("Test Project");
            await Assert.That(result.Id).IsNotEqualTo(Guid.Empty);
        }

        [Test]
        public async Task Handle_CreateWithValidCommand_PersistsProjectToDatabase()
        {
            var handler = new Create.Handler(_authService, _mapper, _dbContext, _telemetryService, _identityResolver);
            var command = new Create.Command { Name = "Persisted Project" };

            var result = await handler.Handle(command, CancellationToken.None);

            var dbProject = await _dbContext.Projects.FindAsync(result.Id);
            await Assert.That(dbProject).IsNotNull();
            await Assert.That(dbProject.Name).IsEqualTo("Persisted Project");
        }

        [Test]
        public async Task Handle_CreateWithValidCommand_CreatesProjectMembership()
        {
            var handler = new Create.Handler(_authService, _mapper, _dbContext, _telemetryService, _identityResolver);
            var command = new Create.Command { Name = "Project With Membership" };

            var result = await handler.Handle(command, CancellationToken.None);

            var membership = await _dbContext.ProjectMemberships
                .FirstOrDefaultAsync(m => m.ProjectId == result.Id);
            await Assert.That(membership).IsNotNull();
            await Assert.That(membership.RoleId).IsEqualTo(ProjectRoleDefaults.ProjectCreatorRoleId);
        }

        [Test]
        public async Task Handle_CreateWhenNotAuthorized_ThrowsForbiddenException()
        {
            A.CallTo(() => _authService.Authorize(A<SystemPermission[]>._, A<CancellationToken>._))
                .Returns(false);

            var handler = new Create.Handler(_authService, _mapper, _dbContext, _telemetryService, _identityResolver);
            var command = new Create.Command { Name = "Unauthorized Project" };

            await Assert.That(async () => await handler.Handle(command, CancellationToken.None))
                .ThrowsExactly<ForbiddenException>();
        }

        #endregion

        #region Get Tests

        [Test]
        public async Task Handle_GetWithExistingProject_ReturnsProject()
        {
            var project = new Domain.Models.Project("Existing Project");
            _dbContext.Projects.Add(project);
            await _dbContext.SaveChangesAsync();

            var handler = new Get.Handler(_authService, _mapper, _dbContext);
            var query = new Get.Query { Id = project.Id };

            var result = await handler.Handle(query, CancellationToken.None);

            await Assert.That(result).IsNotNull();
            await Assert.That(result.Name).IsEqualTo("Existing Project");
            await Assert.That(result.Id).IsEqualTo(project.Id);
        }

        [Test]
        public async Task Handle_GetWithNonExistentProject_ThrowsEntityNotFoundException()
        {
            var handler = new Get.Handler(_authService, _mapper, _dbContext);
            var query = new Get.Query { Id = Guid.NewGuid() };

            await Assert.That(async () => await handler.Handle(query, CancellationToken.None))
                .ThrowsExactly<EntityNotFoundException<Features.Projects.Project>>();
        }

        [Test]
        public async Task Handle_GetWhenNotAuthorized_ThrowsForbiddenException()
        {
            A.CallTo(() => _authService.Authorize<Domain.Models.Project>(A<Guid?>._, A<SystemPermission[]>._, A<ProjectPermission[]>._, A<CancellationToken>._))
                .Returns(false);

            var handler = new Get.Handler(_authService, _mapper, _dbContext);
            var query = new Get.Query { Id = Guid.NewGuid() };

            await Assert.That(async () => await handler.Handle(query, CancellationToken.None))
                .ThrowsExactly<ForbiddenException>();
        }

        #endregion

        #region GetAll Tests

        [Test]
        public async Task Handle_GetAllWithMultipleProjects_ReturnsAllProjects()
        {
            _dbContext.Projects.Add(new Domain.Models.Project("Project 1"));
            _dbContext.Projects.Add(new Domain.Models.Project("Project 2"));
            await _dbContext.SaveChangesAsync();

            var handler = new GetAll.Handler(_authService, _mapper, _dbContext);
            var query = new GetAll.Query { OnlyMine = false };

            var result = await handler.Handle(query, CancellationToken.None);

            await Assert.That(result.Length).IsEqualTo(2);
        }

        [Test]
        public async Task Handle_GetAllWhenOnlyMineTrue_FiltersToUserProjects()
        {
            var handler = new GetAll.Handler(_authService, _mapper, _dbContext);
            var query = new GetAll.Query { OnlyMine = true };

            A.CallTo(() => _authService.GetAuthorizedProjectIds()).Returns(Array.Empty<Guid>());

            var result = await handler.Handle(query, CancellationToken.None);

            await Assert.That(result).IsEmpty();
        }

        [Test]
        public async Task Handle_GetAllWhenUnauthorized_ThrowsForbiddenException()
        {
            A.CallTo(() => _authService.Authorize(A<SystemPermission[]>._, A<CancellationToken>._))
                .Returns(false);

            var handler = new GetAll.Handler(_authService, _mapper, _dbContext);
            var query = new GetAll.Query { OnlyMine = false };

            await Assert.That(async () => await handler.Handle(query, CancellationToken.None))
                .ThrowsExactly<ForbiddenException>();
        }

        #endregion

        #region Edit Tests

        [Test]
        public async Task Handle_EditWithExistingProject_UpdatesName()
        {
            var project = new Domain.Models.Project("Original Name");
            _dbContext.Projects.Add(project);
            await _dbContext.SaveChangesAsync();

            var handler = new Edit.Handler(_authService, _mapper, _dbContext);
            var command = new Edit.Command { Id = project.Id, Name = "Updated Name" };

            var result = await handler.Handle(command, CancellationToken.None);

            await Assert.That(result.Name).IsEqualTo("Updated Name");
            await Assert.That(result.Id).IsEqualTo(project.Id);
        }

        [Test]
        public async Task Handle_EditWithNonExistentProject_ThrowsEntityNotFoundException()
        {
            var handler = new Edit.Handler(_authService, _mapper, _dbContext);
            var command = new Edit.Command { Id = Guid.NewGuid(), Name = "Updated" };

            await Assert.That(async () => await handler.Handle(command, CancellationToken.None))
                .ThrowsExactly<EntityNotFoundException<Features.Projects.Project>>();
        }

        [Test]
        public async Task Handle_EditWithExistingProject_PersistsChangesToDatabase()
        {
            var project = new Domain.Models.Project("Original");
            _dbContext.Projects.Add(project);
            await _dbContext.SaveChangesAsync();

            var handler = new Edit.Handler(_authService, _mapper, _dbContext);
            var command = new Edit.Command { Id = project.Id, Name = "Persisted Update" };

            await handler.Handle(command, CancellationToken.None);

            var dbProject = await _dbContext.Projects.FindAsync(project.Id);
            await Assert.That(dbProject.Name).IsEqualTo("Persisted Update");
        }

        #endregion

        #region Delete Tests

        [Test]
        public async Task Handle_DeleteWithExistingProject_RemovesFromDatabase()
        {
            var project = new Domain.Models.Project("To Delete");
            _dbContext.Projects.Add(project);
            await _dbContext.SaveChangesAsync();

            var handler = new Delete.Handler(_authService, _telemetryService, _dbContext);
            var command = new Delete.Command { Id = project.Id };

            await handler.Handle(command, CancellationToken.None);

            var dbProject = await _dbContext.Projects.FindAsync(project.Id);
            await Assert.That(dbProject).IsNull();
        }

        [Test]
        public async Task Handle_DeleteWithNonExistentProject_ThrowsEntityNotFoundException()
        {
            var handler = new Delete.Handler(_authService, _telemetryService, _dbContext);
            var command = new Delete.Command { Id = Guid.NewGuid() };

            await Assert.That(async () => await handler.Handle(command, CancellationToken.None))
                .ThrowsExactly<EntityNotFoundException<Features.Projects.Project>>();
        }

        [Test]
        public async Task Handle_DeleteWhenNotAuthorized_ThrowsForbiddenException()
        {
            A.CallTo(() => _authService.Authorize<Domain.Models.Project>(A<Guid?>._, A<SystemPermission[]>._, A<ProjectPermission[]>._, A<CancellationToken>._))
                .Returns(false);

            var handler = new Delete.Handler(_authService, _telemetryService, _dbContext);
            var command = new Delete.Command { Id = Guid.NewGuid() };

            await Assert.That(async () => await handler.Handle(command, CancellationToken.None))
                .ThrowsExactly<ForbiddenException>();
        }

        #endregion
    }
}
