// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.ModuleScopes;
using Caster.Api.Features.ModuleScopes;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Caster.Api.Tests.Domain.Services.ModuleScopes;

/// <summary>
/// <c>ModuleAccessService</c> over the test's database: only projects that exist count, and a project's
/// own scopes, its group's and its group membership are read together.
/// </summary>
[Trait("Category", "Unit")]
public class ModuleAccessServiceTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    /// <summary>
    /// A project id that still sits in a caller's claims after the project was deleted must not
    /// count as an unrestricted project, or the caller would be offered every Module.
    /// </summary>
    [Fact]
    public async Task A_deleted_project_in_the_claims_does_not_make_the_caller_unrestricted()
    {
        var scoped = await SeedProject(null, "cmu");
        var deleted = Guid.NewGuid();
        var service = Service(new ClaimsPrincipalBuilder()
            .WithProject(scoped.Id, ProjectPermission.ViewProject)
            .WithProject(deleted, ProjectPermission.ViewProject)
            .Build());

        var access = await service.ForCaller(null, Ct);

        Assert.True(access.Restricted);
        Assert.Equal(["cmu"], access.Paths);
        Assert.False(access.Allows("sei/b"));
    }

    /// <summary>A caller whose only projects are gone gets what a caller with no projects gets: the shared sources, else nothing.</summary>
    [Fact]
    public async Task A_caller_with_only_deleted_projects_gets_only_the_shared_sources()
    {
        var claims = new ClaimsPrincipalBuilder().WithProject(Guid.NewGuid(), ProjectPermission.ViewProject).Build();

        var none = await Service(claims).ForCaller(null, Ct);
        Assert.True(none.Restricted);
        Assert.False(none.Allows("stock/c"));

        var shared = await Service(claims, new TerraformOptions { ModuleSources = [new() { Name = "stock", Url = "https://git.example.com/s.git", Shared = true }] })
            .ForCaller(null, Ct);
        Assert.True(shared.Allows("stock/c"));
        Assert.False(shared.Allows("cmu/a"));
    }

    /// <summary>A project id with no row is not found, not a project with no scopes.</summary>
    [Fact]
    public async Task A_project_that_does_not_exist_has_no_access_to_look_up()
    {
        var service = Service(ClaimsPrincipalBuilder.Anonymous());
        var missing = Guid.NewGuid();

        Assert.Empty(await service.ForProjects([missing], Ct));
        await Assert.ThrowsAsync<EntityNotFoundException<Project>>(() => service.ForProject(missing, Ct));
        await Assert.ThrowsAsync<EntityNotFoundException<Project>>(() => service.ForCaller(missing, Ct));
    }

    /// <summary>A project's own scopes, its group's and whether any applies come from one statement, so a move between them cannot be seen half done.</summary>
    [Fact]
    public async Task The_decision_is_read_in_one_query()
    {
        var group = new ProjectGroup { Id = Guid.NewGuid(), Name = "group" };
        await Seed(group);
        var project = await SeedProject(group, "sei");
        var groupScope = TestData.ModuleScope("Group scope", "cmu");
        await Seed(groupScope, TestData.ProjectGroupModuleScope(group, groupScope));

        var commands = new CommandCounter();
        await using var counted = new CasterContext(new DbContextOptionsBuilder<CasterContext>()
            .UseNpgsql(Db.Database.GetConnectionString())
            .AddInterceptors(commands)
            .Options);
        var service = new ModuleAccessService(
            counted, AuthorizationHarness.CreateCasterAuthorizationService(counted, ClaimsPrincipalBuilder.Anonymous()), Monitor(new()));

        var access = await service.ForProject(project.Id, Ct);

        Assert.Equal(["cmu", "sei"], access.Paths);
        Assert.Equal(1, commands.Count);
    }

    /// <summary>The access shown for a project (GetProjectAccess) is read the same way, in one statement.</summary>
    [Fact]
    public async Task The_access_a_project_reports_is_read_in_one_query()
    {
        var group = new ProjectGroup { Id = Guid.NewGuid(), Name = "group" };
        await Seed(group);
        var project = await SeedProject(group, "sei");
        var groupScope = TestData.ModuleScope("Group scope", "cmu");
        await Seed(groupScope, TestData.ProjectGroupModuleScope(group, groupScope));

        var commands = new CommandCounter();
        await using var counted = new CasterContext(new DbContextOptionsBuilder<CasterContext>()
            .UseNpgsql(Db.Database.GetConnectionString())
            .AddInterceptors(commands)
            .Options);

        var access = await counted.GetProjectAccess(project.Id, new TerraformOptions(), Ct);

        Assert.Equal(["cmu", "sei"], access.Paths);
        Assert.Equal(1, commands.Count);
    }

    /// <summary>A scope held by the project and its group counts once, and a project with neither is unrestricted by default.</summary>
    [Fact]
    public async Task A_project_with_a_scope_only_through_its_group_is_restricted_and_one_with_none_is_not()
    {
        var group = new ProjectGroup { Id = Guid.NewGuid(), Name = "group" };
        await Seed(group);
        var groupScope = TestData.ModuleScope("Group scope", "cmu");
        await Seed(groupScope, TestData.ProjectGroupModuleScope(group, groupScope));
        var inherited = await SeedProject(group);
        var bare = await SeedProject();

        var access = await Service(ClaimsPrincipalBuilder.Anonymous()).ForProjects([inherited.Id, bare.Id], Ct);

        Assert.Equal(["cmu"], access[inherited.Id].Paths);
        Assert.True(access[inherited.Id].Restricted);
        Assert.False(access[bare.Id].Restricted);
    }

    private async Task<Project> SeedProject(ProjectGroup group = null, params string[] ownPaths)
    {
        var project = TestData.Project();
        project.ProjectGroupId = group?.Id;
        await Seed(project);

        if (ownPaths.Length > 0)
        {
            var scope = TestData.ModuleScope(null, ownPaths);
            await Seed(scope, TestData.ProjectModuleScope(project, scope));
        }

        return project;
    }

    private ModuleAccessService Service(System.Security.Claims.ClaimsPrincipal caller, TerraformOptions options = null) =>
        new(Db, AuthorizationHarness.CreateCasterAuthorizationService(Db, caller), Monitor(options ?? new()));

    private static IOptionsMonitor<TerraformOptions> Monitor(TerraformOptions options)
    {
        var monitor = Substitute.For<IOptionsMonitor<TerraformOptions>>();
        monitor.CurrentValue.Returns(options);

        return monitor;
    }

    /// <summary>Counts the commands a context sends to the database.</summary>
    private sealed class CommandCounter : DbCommandInterceptor
    {
        private int _count;

        public int Count => _count;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
