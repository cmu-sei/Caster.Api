// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.Modules;
using Caster.Api.Features.Modules;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Tests.Support;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Caster.Api.Tests.Features.Modules
{
    /// <summary>
    /// <c>GetAll.Handler</c> run directly over the test's database, with the module source seam
    /// (<see cref="IModuleRepositoryService"/>, whose real implementation reaches git and GitLab) a substitute
    /// the test owns: a failed or unconfigured sync still serves what is stored, cancellation is not
    /// swallowed, and a design-filtered listing forces the sync only for a caller its gate admits. The
    /// run-wide host configures no module source, so none of this shows over HTTP; <c>ModuleRequestTests</c>
    /// covers the gate itself there.
    /// </summary>
    [Trait("Category", "Unit")]
    [Trait("Category", "ModuleSources")]
    public class GetAllModulesTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
    {
        private readonly IModuleRepositoryService _modules = Substitute.For<IModuleRepositoryService>();

        [Fact]
        public async Task SyncFailure_ServesTheStoredModules()
        {
            await SeedStoredModule();
            _modules.GetModulesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new ModuleSyncException("module source 'catalog': fatal: repository not found"));

            var modules = await Handler().HandleRequest(new GetAll.Query(), Ct);

            Assert.Equal("Network Segment", Assert.Single(modules).Name);
        }

        [Fact]
        public async Task NoSourceConfigured_ServesTheStoredModules()
        {
            await SeedStoredModule();
            _modules.GetModulesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new ModuleSourceNotConfiguredException());

            var modules = await Handler().HandleRequest(new GetAll.Query(), Ct);

            Assert.Single(modules);
        }

        [Fact]
        public async Task Cancellation_StillPropagates()
        {
            await SeedStoredModule();
            _modules.GetModulesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new OperationCanceledException());

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                Handler().HandleRequest(new GetAll.Query(), Ct));
        }

        /// <summary>
        /// A caller allowed on the design's project, or holding ViewProjects, and allowed the catalog (a project
        /// membership or ViewModules) is answered the design's modules, after a sync that forced the update the
        /// query asked for. Rows: <c>project</c> ViewProject on project A; <c>system</c> ViewModules and
        /// ViewProjects; <c>foreign</c> asks for project B's design.
        /// </summary>
        [Theory]
        [InlineData("project", false)]
        [InlineData("system", false)]
        [InlineData("system", true)]
        public async Task Design_filtered_modules_sync_and_list_for_a_caller_with_catalog_and_design_access(string access, bool foreign)
        {
            var graph = await SeedGraph();

            var modules = await Handler(Caller(access, graph.ProjectA.Id)).Handle(DesignQuery(graph, foreign), Ct);

            Assert.Equal(graph.Module.Id, Assert.Single(modules).Id);
            await _modules.Received(1).GetModulesAsync(true, Arg.Any<CancellationToken>());
        }

        /// <summary>
        /// A caller without view access to the design's project, or without the catalog, is refused before any
        /// sync is asked for. Rows: <c>project</c> ViewProject on project A, asking for project B's design;
        /// <c>catalog</c> ViewModules alone; <c>edit</c> EditProject on project A; <c>viewprojects</c>
        /// ViewProjects alone.
        /// </summary>
        [Theory]
        [InlineData("project", true)]
        [InlineData("catalog", false)]
        [InlineData("catalog", true)]
        [InlineData("edit", false)]
        [InlineData("viewprojects", false)]
        public async Task Design_filtered_modules_are_refused_before_any_sync_without_catalog_and_design_access(string access, bool foreign)
        {
            var graph = await SeedGraph();

            await Assert.ThrowsAsync<ForbiddenException>(() =>
                Handler(Caller(access, graph.ProjectA.Id)).Handle(DesignQuery(graph, foreign), Ct));

            await _modules.DidNotReceive().GetModulesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>());
        }

        private async Task<TwoProjectGraph> SeedGraph()
        {
            var savedBy = TestData.User();
            await Seed(savedBy);

            return await TwoProjectGraph.SeedAsync(Db, savedBy.Id, Ct);
        }

        /// <summary>A forced listing of project A's design, or of project B's when <paramref name="foreign"/>.</summary>
        private static GetAll.Query DesignQuery(TwoProjectGraph graph, bool foreign) =>
            new() { DesignId = foreign ? graph.DesignB.Id : graph.DesignA.Id, ForceUpdate = true };

        /// <summary>The principal the claims transformer would produce for each row's caller.</summary>
        private static ClaimsPrincipal Caller(string access, Guid projectId) => access switch
        {
            "project" => new ClaimsPrincipalBuilder().WithProject(projectId, ProjectPermission.ViewProject).Build(),
            "edit" => new ClaimsPrincipalBuilder().WithProject(projectId, ProjectPermission.EditProject).Build(),
            "catalog" => new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ViewModules).Build(),
            "system" => new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ViewModules, SystemPermission.ViewProjects).Build(),
            _ => new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ViewProjects).Build()
        };

        /// <summary>The one module every test here lists, stored before the sync is asked for.</summary>
        private async Task SeedStoredModule()
        {
            var module = TestData.Module("Network Segment");
            module.Path = "catalog/network-segment";
            await Seed(module);
        }

        /// <summary>
        /// The handler over the real mapper and the real authorization service, deciding for
        /// <paramref name="caller"/> (one holding nothing by default, for the tests that call
        /// <c>HandleRequest</c>, which runs after the gate).
        /// </summary>
        private GetAll.Handler Handler(ClaimsPrincipal caller = null) =>
            new(
                AuthorizationHarness.CreateCasterAuthorizationService(Db, caller ?? ClaimsPrincipalBuilder.Anonymous()),
                TestMapper.Mapper,
                Db,
                _modules,
                new RecordingLogger<GetAll>());
    }
}
