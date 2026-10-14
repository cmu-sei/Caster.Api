// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Caster.Api.Data;
using Caster.Api.Domain.Services.Modules;
using Caster.Api.Features.Modules;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Caster.Api.Tests.Unit.Modules
{
    [Trait("Category", "Unit")]
    [Trait("Category", "ModuleSources")]
    public class GetAllModulesTests : IDisposable
    {
        private readonly CasterContext _db;
        private readonly IModuleRepositoryService _modules = Substitute.For<IModuleRepositoryService>();
        private readonly GetAll.Handler _sut;

        public GetAllModulesTests()
        {
            _db = new CasterContext(new DbContextOptionsBuilder<CasterContext>()
                .UseInMemoryDatabase($"caster_getall_{Guid.NewGuid():N}")
                .Options);

            _db.Modules.Add(new Domain.Models.Module { Path = "catalog/network-segment", Name = "Network Segment" });
            _db.SaveChanges();

            var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();

            _sut = new GetAll.Handler(
                Substitute.For<ICasterAuthorizationService>(),
                mapper,
                _db,
                _modules,
                Substitute.For<ILogger<GetAll>>());
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task SyncFailure_ServesTheStoredModules()
        {
            _modules.GetModulesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new ModuleSyncException("module source 'catalog': fatal: repository not found"));

            var modules = await _sut.HandleRequest(new GetAll.Query(), CancellationToken.None);

            Assert.Equal("Network Segment", Assert.Single(modules).Name);
        }

        [Fact]
        public async Task NoSourceConfigured_ServesTheStoredModules()
        {
            _modules.GetModulesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new ModuleSourceNotConfiguredException());

            var modules = await _sut.HandleRequest(new GetAll.Query(), CancellationToken.None);

            Assert.Single(modules);
        }

        [Fact]
        public async Task Cancellation_StillPropagates()
        {
            _modules.GetModulesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new OperationCanceledException());

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                _sut.HandleRequest(new GetAll.Query(), CancellationToken.None));
        }
    }
}
