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

        /// <summary>A member holding ViewProject on the design's project is answered its modules, after a sync that forced the update the query asked for.</summary>
        [Fact]
        public async Task Design_filtered_modules_sync_and_list_for_a_member_holding_ViewProject_on_the_designs_project()
        {
            var graph = await SeedGraph();
            var caller = new ClaimsPrincipalBuilder().WithProject(graph.ProjectA.Id, ProjectPermission.ViewProject).Build();

            var modules = await Handler(caller).Handle(DesignQuery(graph.DesignA), Ct);

            Assert.Equal(graph.Module.Id, Assert.Single(modules).Id);
            await _modules.Received(1).GetModulesAsync(true, Arg.Any<CancellationToken>());
        }

        /// <summary>A caller holding ViewModules and ViewProjects is answered the design's modules after the forced sync.</summary>
        [Fact]
        public async Task Design_filtered_modules_sync_and_list_for_a_caller_holding_ViewModules_and_ViewProjects()
        {
            var graph = await SeedGraph();
            var caller = new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ViewModules, SystemPermission.ViewProjects).Build();

            var modules = await Handler(caller).Handle(DesignQuery(graph.DesignA), Ct);

            Assert.Equal(graph.Module.Id, Assert.Single(modules).Id);
            await _modules.Received(1).GetModulesAsync(true, Arg.Any<CancellationToken>());
        }

        /// <summary>The system permissions answer for a design of any project: project B's design is listed too.</summary>
        [Fact]
        public async Task Design_filtered_modules_sync_and_list_for_a_caller_holding_ViewModules_and_ViewProjects_for_a_design_of_another_project()
        {
            var graph = await SeedGraph();
            var caller = new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ViewModules, SystemPermission.ViewProjects).Build();

            var modules = await Handler(caller).Handle(DesignQuery(graph.DesignB), Ct);

            Assert.Equal(graph.Module.Id, Assert.Single(modules).Id);
            await _modules.Received(1).GetModulesAsync(true, Arg.Any<CancellationToken>());
        }

        /// <summary>A member holding ViewProject on project A asking for project B's design is refused before any sync.</summary>
        [Fact]
        public async Task Design_filtered_modules_are_refused_before_any_sync_for_a_member_holding_ViewProject_only_on_another_project()
        {
            var graph = await SeedGraph();
            var caller = new ClaimsPrincipalBuilder().WithProject(graph.ProjectA.Id, ProjectPermission.ViewProject).Build();

            await Assert.ThrowsAsync<ForbiddenException>(() => Handler(caller).Handle(DesignQuery(graph.DesignB), Ct));

            await _modules.DidNotReceive().GetModulesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>());
        }

        /// <summary>ViewModules opens the catalog but not a design: refused before any sync.</summary>
        [Fact]
        public async Task Design_filtered_modules_are_refused_before_any_sync_for_a_caller_holding_only_ViewModules()
        {
            var graph = await SeedGraph();
            var caller = new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ViewModules).Build();

            await Assert.ThrowsAsync<ForbiddenException>(() => Handler(caller).Handle(DesignQuery(graph.DesignA), Ct));

            await _modules.DidNotReceive().GetModulesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>());
        }

        /// <summary>ViewModules alone is refused whichever project the design is in.</summary>
        [Fact]
        public async Task Design_filtered_modules_are_refused_before_any_sync_for_a_caller_holding_only_ViewModules_for_a_design_of_another_project()
        {
            var graph = await SeedGraph();
            var caller = new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ViewModules).Build();

            await Assert.ThrowsAsync<ForbiddenException>(() => Handler(caller).Handle(DesignQuery(graph.DesignB), Ct));

            await _modules.DidNotReceive().GetModulesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>());
        }

        /// <summary>EditProject on the design's project opens the catalog but not the design: refused before any sync.</summary>
        [Fact]
        public async Task Design_filtered_modules_are_refused_before_any_sync_for_a_member_holding_only_EditProject_on_the_designs_project()
        {
            var graph = await SeedGraph();
            var caller = new ClaimsPrincipalBuilder().WithProject(graph.ProjectA.Id, ProjectPermission.EditProject).Build();

            await Assert.ThrowsAsync<ForbiddenException>(() => Handler(caller).Handle(DesignQuery(graph.DesignA), Ct));

            await _modules.DidNotReceive().GetModulesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>());
        }

        /// <summary>ViewProjects opens the design but not the catalog: refused before any sync.</summary>
        [Fact]
        public async Task Design_filtered_modules_are_refused_before_any_sync_for_a_caller_holding_only_ViewProjects()
        {
            var graph = await SeedGraph();
            var caller = new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ViewProjects).Build();

            await Assert.ThrowsAsync<ForbiddenException>(() => Handler(caller).Handle(DesignQuery(graph.DesignA), Ct));

            await _modules.DidNotReceive().GetModulesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>());
        }

        private async Task<TwoProjectGraph> SeedGraph()
        {
            var savedBy = TestData.User();
            await Seed(savedBy);

            return await TwoProjectGraph.SeedAsync(Db, savedBy.Id, Ct);
        }

        /// <summary>A forced listing of <paramref name="design"/>'s modules.</summary>
        private static GetAll.Query DesignQuery(Design design) => new() { DesignId = design.Id, ForceUpdate = true };

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
